using System.Globalization;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Imports;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Customers;

public sealed class ExistingDueService(RetailOperations r, PartyService parties)
{
    private async Task AccessAsync(Guid branch, CancellationToken ct)
    {
        await r.BranchAsync(branch, "ledger.adjust", ct);
        await r.PermissionAsync("customers.credit.view", ct);
    }
    public Task<Guid> CreateAsync(ExistingDueRequest request, string key, CancellationToken ct) =>
        r.ExecuteAsync("existing-due-create", key, request, async () => {
            await AccessAsync(request.BranchId, ct);
            return await CreateInternalAsync(request, ct);
        }, ct);

    private async Task ValidateAsync(ExistingDueRequest request, CancellationToken ct)
    {
        RetailOperations.Text(request.Reference, "Unique register / old invoice reference", 100);
        RetailOperations.Text(request.Description, "Device / reason", 500);
        RetailOperations.Check(request.FinanceReference.Length <= 200, "Finance reference must fit within 200 characters.");
        RetailOperations.Money(request.Amount, true);
        RetailOperations.Check(request.BusinessDate <= await r.TodayAsync(ct) && request.BusinessDate >= new DateOnly(2000,1,1), "Original date must be between 2000 and today.");
        RetailOperations.Check(request.DueDate >= request.BusinessDate, "Promise / due date cannot precede the original date.");
        var reference = request.Reference.Trim().ToUpperInvariant();
        RetailOperations.Check(!await r.Db.Set<ExistingDue>().AnyAsync(x => x.BranchId == request.BranchId && x.Reference == reference, ct), "This register reference already exists. Check the original entry; do not import it twice.", "DUPLICATE_EXISTING_DUE");
        RetailOperations.Check(!await r.Db.Set<Sale>().AnyAsync(x => x.BranchId == request.BranchId && (x.Number.ToUpper() == reference || x.Reference.ToUpper() == reference), ct), "This invoice is already in Invora. Use its existing invoice due instead of bringing it forward again.");
    }
    private async Task<Guid> CreateInternalAsync(ExistingDueRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        await r.PartyAsync(request.CustomerId, PartyKind.Customer, ct);
        var due = new ExistingDue { BranchId=request.BranchId, CustomerId=request.CustomerId, ActorId=r.Actor,
            Reference=request.Reference.Trim().ToUpperInvariant(), Description=request.Description.Trim(), FinanceReference=request.FinanceReference.Trim(),
            Amount=request.Amount, BusinessDate=request.BusinessDate, DueDate=request.DueDate };
        r.Db.Add(due);
        r.Ledger(due.BranchId, due.CustomerId, await r.TodayAsync(ct), "OpeningDue", due.Amount, 0,
            note:$"Existing due · {due.Reference} · {due.Description}", existingDue:due.Id);
        r.Audit("EXISTING_DUE_RECORDED", due.Id, request);
        await r.Db.SaveChangesAsync(ct);
        return due.Id;
    }
    public Task<Guid> CancelAsync(Guid id, ReasonRequest request, string key, CancellationToken ct) =>
        r.ExecuteAsync("existing-due-cancel", key, new { id, request }, async () => {
            var due=await r.Db.Set<ExistingDue>().SingleOrDefaultAsync(x=>x.Id==id,ct) ?? throw new DomainException("NOT_FOUND","Existing due not found.");
            await AccessAsync(due.BranchId,ct);
            RetailOperations.Text(request.Reason,"Reason for correcting this entry");
            RetailOperations.Check(!await r.Db.Set<LedgerEntry>().AnyAsync(x=>x.ExistingDueId==id&&x.Kind=="OpeningDueReversal",ct),"This entry is already cancelled.");
            RetailOperations.Check(await r.ExistingDueBalanceAsync(due,ct)==due.Amount,"Reverse allocated receipts before cancelling this entry. Keep actual money received on the customer account.");
            var original=await r.Db.Set<LedgerEntry>().SingleAsync(x=>x.ExistingDueId==id&&x.Kind=="OpeningDue",ct);
            r.Ledger(due.BranchId,due.CustomerId,await r.TodayAsync(ct),"OpeningDueReversal",0,due.Amount,reverse:original.Id,note:request.Reason,existingDue:id);
            r.Audit("EXISTING_DUE_CANCELLED",id,new{request.Reason}); return id;
        },ct);

    // Indian country prefix is equivalent to the local ten-digit number. Never match names loosely.
    private static string Phone(string value, bool optional=false)
    {
        RetailOperations.Check(value.All(c=>char.IsAsciiDigit(c)||"+ ()-".Contains(c)),"Phone contains invalid characters.");
        var digits=new string(value.Where(char.IsAsciiDigit).ToArray());
        RetailOperations.Check(optional && digits.Length==0 || digits.Length is >=7 and <=15,"Phone must contain 7–15 digits.");
        return digits.Length==12 && digits.StartsWith("91",StringComparison.Ordinal) ? digits[2..] : digits;
    }
    private static Party? Match(ExistingDueImportRow row,IEnumerable<Party> customers)
    {
        var phones=new[]{row.Phone,row.AlternatePhone}.Where(x=>x.Length>0).ToArray();
        var matches=customers.Where(p=>phones.Contains(Phone(p.Phone,true)) || p.AlternatePhone.Length>0&&phones.Contains(Phone(p.AlternatePhone,true))).ToArray();
        RetailOperations.Check(matches.Length<=1,"These numbers match multiple customers. Correct the contacts before importing.");
        var match=matches.SingleOrDefault();
        RetailOperations.Check(match is null || match.IsActive && string.Equals(match.Name.Trim(),row.Name.Trim(),StringComparison.OrdinalIgnoreCase),"Phone matches a different customer name or an inactive account. Confirm the customer details before importing.");
        // Do not silently replace or discard an alternate number from a reviewed register.
        RetailOperations.Check(match is null || row.AlternatePhone.Length==0 || new[]{Phone(match.Phone,true),Phone(match.AlternatePhone,true)}.Contains(row.AlternatePhone),"Second number differs from the existing contact. Update the customer first, then validate again.");
        return match;
    }
    public Task<ExistingDueImportPreview> ValidateImportAsync(ExistingDueImportRequest request,string key,CancellationToken ct) =>
        r.ExecuteAsync<ExistingDueImportPreview>("existing-dues-import-preview",key,request,async()=>{
            await AccessAsync(request.BranchId,ct);await r.PermissionAsync("customers.manage",ct);
            RetailOperations.Check(request.Csv.Length is >0 and <=1000000,"CSV must fit within 1 MB.");
            var csv=ImportService.Parse(request.Csv);RetailOperations.Check(csv.Count is >1 and <=501,"Use a header and 1–500 records per import.");
            var headers=csv[0].Select(x=>x.Trim().TrimStart('\uFEFF')).ToArray();
            var required=new[]{"name","phone","alternatePhone","reference","description","financeReference","amount","originalDate","dueDate"};
            RetailOperations.Check(headers.Length==required.Length && headers.Distinct().Count()==headers.Length && required.All(headers.Contains),"Use the provided template columns exactly.");
            var customers=await r.Db.Set<Party>().Where(x=>x.Kind==PartyKind.Customer).ToListAsync(ct);
            var virtualCustomers=new List<Party>(customers);
            var records=new List<ExistingDueImportRow>();var errors=new List<ExistingDueImportError>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(var i=1;i<csv.Count;i++)try{
                RetailOperations.Check(csv[i].Length==headers.Length,"Column count does not match the header.");
                var cells=headers.Zip(csv[i]).ToDictionary(x=>x.First,x=>x.Second.Trim());
                RetailOperations.Text(cells["name"],"Customer name",200);
                var phone=Phone(cells["phone"]);var alternate=Phone(cells["alternatePhone"],true);
                RetailOperations.Check(alternate.Length==0||alternate!=phone,"Use two different phone numbers.");
                var amount=decimal.Parse(cells["amount"],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture);
                DateOnly Date(string column)=>DateOnly.ParseExact(cells[column],"yyyy-MM-dd",CultureInfo.InvariantCulture);
                var row=new ExistingDueImportRow(cells["name"],phone,alternate,cells["reference"].ToUpperInvariant(),cells["description"],cells["financeReference"],amount,Date("originalDate"),Date("dueDate"),null);
                await ValidateAsync(new(request.BranchId,Guid.Empty,row.Reference,row.Description,row.FinanceReference,row.Amount,row.BusinessDate,row.DueDate),ct);
                RetailOperations.Check(seen.Add(row.Reference),"Register reference repeats in this file.");
                var match=Match(row,virtualCustomers);
                row=row with{CustomerId=customers.Any(x=>x.Id==match?.Id)?match!.Id:null};
                if(match is null)virtualCustomers.Add(new Party{Kind=PartyKind.Customer,Name=row.Name,Phone=row.Phone,AlternatePhone=row.AlternatePhone});
                records.Add(row);
            }catch(Exception ex)when(ex is DomainException or FormatException or OverflowException){errors.Add(new(i+1,ex is DomainException?ex.Message:"Use a plain amount and dates in yyyy-MM-dd format."));}
            var job=new ImportJob{BranchId=request.BranchId,ActorId=r.Actor,Kind="existing-dues",RowsJson=RetailOperations.Json(records),ErrorsJson=RetailOperations.Json(errors),RowCount=csv.Count-1,Status=errors.Count==0?"Validated":"Invalid"};
            r.Db.Add(job);r.Audit("EXISTING_DUES_IMPORT_REVIEWED",job.Id,new{job.RowCount,errorCount=errors.Count});
            return new(job.Id,job.Status,job.RowCount,records.Sum(x=>x.Amount),errors.ToArray(),records.ToArray());
        },ct);
    public Task<ExistingDueImportResult> CommitImportAsync(Guid id,string key,CancellationToken ct) =>
        r.ExecuteAsync<ExistingDueImportResult>("existing-dues-import-commit",key,new{id},async()=>{
            var job=await r.Db.Set<ImportJob>().SingleOrDefaultAsync(x=>x.Id==id&&x.ActorId==r.Actor&&x.Kind=="existing-dues",ct) ?? throw new DomainException("NOT_FOUND","Import not found.");
            await AccessAsync(job.BranchId!.Value,ct);await r.PermissionAsync("customers.manage",ct);
            RetailOperations.Check(job.Status=="Validated"&&job.CreatedAtUtc>DateTimeOffset.UtcNow.AddDays(-1),"Validate an uncommitted file again before importing.");
            var customers=await r.Db.Set<Party>().Where(x=>x.Kind==PartyKind.Customer).ToListAsync(ct);var created=new HashSet<Guid>();
            foreach(var row in RetailOperations.Read<ExistingDueImportRow[]>(job.RowsJson)){
                var match=Match(row,customers);
                RetailOperations.Check(row.CustomerId==match?.Id || row.CustomerId is null && match is not null && created.Contains(match.Id),"Customer matching changed after preview. Validate the file again.");
                if(match is null){var customerId=await parties.CreateInternalAsync(PartyKind.Customer,new(row.Name,row.Phone,"","","","",null,row.AlternatePhone),ct);await r.Db.SaveChangesAsync(ct);match=await r.Db.Set<Party>().SingleAsync(x=>x.Id==customerId,ct);customers.Add(match);created.Add(customerId);}
                await CreateInternalAsync(new(job.BranchId.Value,match.Id,row.Reference,row.Description,row.FinanceReference,row.Amount,row.BusinessDate,row.DueDate),ct);
            }
            job.Status="Committed";r.Audit("EXISTING_DUES_IMPORTED",job.Id,new{job.RowCount});return new(job.Id,job.Status,job.RowCount);
        },ct);
}
