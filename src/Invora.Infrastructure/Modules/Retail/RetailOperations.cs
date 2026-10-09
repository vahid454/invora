using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Invora.Application.Abstractions;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Identity;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Retail;
public sealed class RetailOperations(InvoraDbContext db, ICurrentUser user)
{
    private readonly HashSet<string> requiredPermissions=[];
    private readonly HashSet<Guid> requiredBranches=[];
    public InvoraDbContext Db => db;
    public Guid Actor => user.UserId;
    public bool Can(string permission) => user.Permissions.Contains(permission);
    public static void Check(bool valid,string message,string code="VALIDATION_FAILED") { if(!valid)throw new DomainException(code,message); }
    public static void Text(string? text,string label,int max=2000) => Check(!string.IsNullOrWhiteSpace(text) && text.Length<=max,label+" is required and must fit its limit.");
    public static void Money(decimal amount,bool positive=false) => Check(amount >= (positive ? 0.01m : 0) && amount<=99999999999999m && decimal.Round(amount,2)==amount,"Amounts must be nonnegative two-decimal values within the supported range.");
    public static (string[] Terms,string? Ram,string? Storage) SearchParts(string? search){
        var text=(search??"").Trim().ToUpperInvariant();Check(text.Length<=300,"Search must be at most 300 characters.");
        var memory=System.Text.RegularExpressions.Regex.Match(text,@"\b(\d{1,2})(?:\s*GB)?\s*/\s*(\d{1,4})\s*(GB|TB)?\b");
        string? ram=null,storage=null;if(memory.Success){ram=memory.Groups[1].Value;storage=memory.Groups[2].Value+(memory.Groups[3].Value=="TB"?"TB":"");text=text.Remove(memory.Index,memory.Length);}
        return(System.Text.RegularExpressions.Regex.Split(text,@"\s+").Where(x=>x.Length>0).ToArray(),ram,storage);
    }
    public static void Page(int page,int size) => Check(page is >0 and <=100000 && size is >0 and <=100,"Invalid page; pageSize must be 1–100.");
    public static string Json(object value)=>JsonSerializer.Serialize(value);
    public static T Read<T>(string json)=>JsonSerializer.Deserialize<T>(json)??throw new InvalidOperationException("Persisted document is invalid.");
    public void Audit(string action,Guid id,object? details=null)=>db.AccessAudits.Add(new AccessAudit{ActorId=Actor,Action=action,ResourceId=id,BranchIds=requiredBranches.ToArray(),DetailsJson=Json(details??new{})});
    public async Task PermissionAsync(string permission,CancellationToken ct)
    {
        Check(await db.StaffUsers.AnyAsync(x=>x.Id==Actor && x.IsActive && x.Permissions.Contains(permission),ct),"Permission is required.","ACCESS_DENIED");requiredPermissions.Add(permission);
    }
    public async Task BranchAsync(Guid branchId,string permission,CancellationToken ct)
    {
        await PermissionAsync(permission,ct);
        Check(await db.StaffBranches.AnyAsync(x=>x.UserId==Actor && x.BranchId==branchId && db.Branches.Any(b=>b.Id==branchId && b.IsActive),ct),"Resource not found.","NOT_FOUND");requiredBranches.Add(branchId);
    }
    public async Task<Party> PartyAsync(Guid id,PartyKind kind,CancellationToken ct) => await db.Set<Party>().SingleOrDefaultAsync(x=>x.Id==id && x.Kind==kind && x.IsActive,ct)??throw new DomainException("NOT_FOUND","Party not found.");
    public async Task<T> ExecuteAsync<T>(string operation,string key,object input,Func<Task<T>> work,CancellationToken ct)
    {
        Text(key,"Idempotency-Key",100);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(input))));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72619431)",ct);
        Check(await db.StaffUsers.AnyAsync(x=>x.Id==Actor && x.IsActive,ct),"User unavailable.","ACCESS_DENIED");
        var previous=await db.Set<OperationReceipt>().SingleOrDefaultAsync(x=>x.UserId==Actor && x.Operation==operation && x.Key==key,ct);
        if(previous is not null){Check(previous.RequestHash==hash,"Key was used for another request.","IDEMPOTENCY_CONFLICT");foreach(var permission in previous.Permissions)await PermissionAsync(permission,ct);foreach(var branch in previous.BranchIds)Check(await db.StaffBranches.AnyAsync(x=>x.UserId==Actor && x.BranchId==branch && db.Branches.Any(b=>b.Id==branch && b.IsActive),ct),"Resource not found.","NOT_FOUND");return Read<T>(previous.ResultJson);}
        var result=await work();
        db.Set<OperationReceipt>().Add(new(){UserId=Actor,Operation=operation,Key=key,RequestHash=hash,ResultJson=Json(result!),Permissions=requiredPermissions.ToArray(),BranchIds=requiredBranches.ToArray()});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return result;
    }
    public async Task<int> FiscalYearAsync(DateOnly date,CancellationToken ct){var business=await db.Businesses.SingleAsync(ct);return date.Month>=business.FinancialYearStartMonth?date.Year:date.Year-1;}
    public async Task<string> NumberAsync(Guid branchId,string kind,DateOnly date,CancellationToken ct)
    {
        var business=await db.Businesses.SingleAsync(ct);var branch=await db.Branches.SingleAsync(x=>x.Id==branchId,ct);
        var year=date.Month>=business.FinancialYearStartMonth?date.Year:date.Year-1;
        var taxDocument=kind is "INV" or "CRN";
        if(taxDocument)branchId=await db.Branches.OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Select(x=>x.Id).FirstAsync(ct);
        var series=taxDocument?"GST-"+kind:kind;
        var seq=db.Set<DocumentSequence>().Local.SingleOrDefault(x=>x.BranchId==branchId && x.Kind==series && x.FinancialYear==year)??await db.Set<DocumentSequence>().SingleOrDefaultAsync(x=>x.BranchId==branchId && x.Kind==series && x.FinancialYear==year,ct);
        if(seq is null){seq=new(){BranchId=branchId,Kind=series,FinancialYear=year};db.Add(seq);}seq.LastNumber++;
        if(taxDocument){Check(seq.LastNumber<=999999,"The annual invoice series is exhausted. Configure a reviewed additional series before posting.");return $"{(kind=="INV"?"I":"C")}-{date.ToString("ddMMMyy",System.Globalization.CultureInfo.InvariantCulture)}-{seq.LastNumber:D6}";}
        return $"{kind}-{branch.Code}-{year%100:D2}-{seq.LastNumber:D6}";
    }
    public async Task<string> InvoiceReferenceAsync(Guid branchId,string kind,DateOnly date,string number,CancellationToken ct)
    {
        var business=await db.Businesses.SingleAsync(ct);var branch=await db.Branches.SingleAsync(x=>x.Id==branchId,ct);
        var words=System.Text.RegularExpressions.Regex.Matches(business.TradeName.ToUpperInvariant(),"[A-Z0-9]+");
        var shop=string.Concat(words.Take(4).Select(x=>x.Value[0]));if(shop.Length==0)shop="SHOP";
        return $"{kind}-{shop}-{branch.Code}-{date.ToString("ddMMMyy",System.Globalization.CultureInfo.InvariantCulture)}-{number.Split('-').Last()}";
    }
    public void Ledger(Guid branch,Guid party,DateOnly date,string kind,decimal debit,decimal credit,Guid? sale=null,Guid? purchase=null,Guid? payment=null,Guid? saleReturn=null,Guid? reverse=null,string note="")
    {
        Check(debit>0 && credit==0 || credit>0 && debit==0,"Ledger entry must have one positive side.");
        db.Add(new LedgerEntry{BranchId=branch,PartyId=party,BusinessDate=date,Kind=kind,Debit=debit,Credit=credit,SaleId=sale,PurchaseId=purchase,PaymentId=payment,SaleReturnId=saleReturn,ReversesEntryId=reverse,Note=note,ActorId=Actor});
    }
    public void Movement(Guid branch,Guid variant,int qty,string kind,Guid? unit=null,Guid? purchase=null,Guid? sale=null,Guid? saleReturn=null,Guid? transfer=null,string note="")=>db.Add(new InventoryMovement{BranchId=branch,ProductVariantId=variant,StockUnitId=unit,Quantity=qty,Kind=kind,PurchaseId=purchase,SaleId=sale,SaleReturnId=saleReturn,TransferId=transfer,Note=note,ActorId=Actor});
    public async Task<decimal> TradingBalanceAsync(Guid branch,Guid party,CancellationToken ct)=>await db.Set<LedgerEntry>().Where(x=>x.BranchId==branch && x.PartyId==party && !x.Kind.StartsWith("LenDen")).SumAsync(x=>x.Debit-x.Credit,ct);
    public async Task<decimal> BalanceAsync(Guid branch,Guid party,CancellationToken ct)=>await db.Set<LedgerEntry>().Where(x=>x.BranchId==branch && x.PartyId==party).SumAsync(x=>x.Debit-x.Credit,ct);
    public async Task<decimal> AllocatedAsync(Guid? sale,Guid? purchase,CancellationToken ct)
    {
        var allocations=db.Set<PaymentAllocation>().Where(x=>sale!=null?x.SaleId==sale:x.PurchaseId==purchase);
        var cash = await allocations.SumAsync(x=>x.Amount-db.Set<AllocationReversal>().Where(r=>r.PaymentAllocationId==x.Id).Sum(r=>(decimal?)r.Amount).GetValueOrDefault(),ct);
        return cash + (sale is not null ? await db.Set<SaleNonCashSettlement>().Where(x=>x.SaleId==sale).SumAsync(x=>x.Amount-db.Set<NonCashSettlementReversal>().Where(y=>y.SaleNonCashSettlementId==x.Id).Sum(y=>(decimal?)y.Amount).GetValueOrDefault(),ct) : 0);
    }
    public async Task<decimal> SaleDueAsync(Sale sale,CancellationToken ct)=>Math.Max(0,sale.Total-await db.Set<SaleReturn>().Where(x=>x.SaleId==sale.Id).SumAsync(x=>x.Credit,ct)-await AllocatedAsync(sale.Id,null,ct));
    public async Task<DateOnly> TodayAsync(CancellationToken ct){var business=await db.Businesses.SingleAsync(ct);return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone)).Date);}
    public async Task CaptureReceiptAsync(Payment payment,CancellationToken ct){var seller=await SettingsAsync(ct);var business=await db.Businesses.SingleAsync(ct);var party=await db.Set<Party>().SingleAsync(x=>x.Id==payment.PartyId,ct);payment.ReceiptJson=Json(new PaymentReceipt(await NumberAsync(payment.BranchId,"PAY",payment.BusinessDate,ct),payment.BusinessDate,business.TradeName,seller.Address,party.Name,party.Phone,payment.Amount,payment.Direction,payment.Method.ToString(),payment.Reference,payment.Note));}
    public async Task<SettingsRequest> SettingsAsync(CancellationToken ct)
    {
        var settings=await db.Set<BusinessSettings>().SingleOrDefaultAsync(ct);return (settings is null?new SettingsRequest():Read<SettingsRequest>(settings.Json)) with {WarrantyTerms=WarrantyPolicy.Terms};
    }
}
