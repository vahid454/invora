using System.Globalization;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Exports;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Customers;

public sealed class CollectionService(RetailOperations r)
{
    private async Task<CollectionRow[]> RowsAsync(Guid branch,CancellationToken ct)
    {
        await r.BranchAsync(branch,"customers.credit.view",ct);
        var oldQuery=r.Db.Set<ExistingDue>().AsNoTracking().Where(x=>x.BranchId==branch);
        var salesQuery=r.Db.Set<Sale>().AsNoTracking().Where(x=>x.BranchId==branch&&x.Status!="Draft"&&x.Status!="Cancelled"&&x.Status!="Abandoned");
        RetailOperations.Check(await oldQuery.CountAsync(ct)+await salesQuery.CountAsync(ct)<=20000,"Collection register exceeds 20,000 documents. Use customer statements until an archived reporting period is configured.");
        var old=await (from d in oldQuery join p in r.Db.Set<Party>() on d.CustomerId equals p.Id
            let cancelled=r.Db.Set<LedgerEntry>().Any(e=>e.ExistingDueId==d.Id&&e.Kind=="OpeningDueReversal")
            let paid=r.Db.Set<PaymentAllocation>().Where(a=>a.ExistingDueId==d.Id).Sum(a=>(decimal?)(a.Amount-(r.Db.Set<AllocationReversal>().Where(v=>v.PaymentAllocationId==a.Id).Sum(v=>(decimal?)v.Amount)??0)))??0
            select new CollectionRow(d.Id,"existing-due",p.Id,p.Name,p.Phone,p.AlternatePhone,d.Reference,d.Description,d.FinanceReference,d.BusinessDate,d.DueDate,d.Amount,paid,cancelled?0:d.Amount-paid,0,0,cancelled,null,null)).ToArrayAsync(ct);
        var sales=await (from d in salesQuery join p in r.Db.Set<Party>() on d.CustomerId equals p.Id
            let credit=r.Db.Set<SaleReturn>().Where(a=>a.SaleId==d.Id).Sum(a=>(decimal?)a.Credit)??0
            let paid=r.Db.Set<PaymentAllocation>().Where(a=>a.SaleId==d.Id).Sum(a=>(decimal?)(a.Amount-(r.Db.Set<AllocationReversal>().Where(v=>v.PaymentAllocationId==a.Id).Sum(v=>(decimal?)v.Amount)??0)))??0
            let nonCash=r.Db.Set<SaleNonCashSettlement>().Where(a=>a.SaleId==d.Id).Sum(a=>(decimal?)(a.Amount-(r.Db.Set<NonCashSettlementReversal>().Where(v=>v.SaleNonCashSettlementId==a.Id).Sum(v=>(decimal?)v.Amount)??0)))??0
            select new CollectionRow(d.Id,"invoice",p.Id,p.Name,p.Phone,p.AlternatePhone,d.Number,"Invoice · "+d.Reference,"",d.BusinessDate,d.DueDate,d.Total-credit,paid+nonCash,d.Total-credit-paid-nonCash,0,0,false,null,null)).ToArrayAsync(ct);
        var all=old.Concat(sales).ToArray();
        var balances=await r.Db.Set<LedgerEntry>().Where(x=>x.BranchId==branch&&!x.Kind.StartsWith("LenDen")).GroupBy(x=>x.PartyId).Select(g=>new{Id=g.Key,Amount=g.Sum(x=>x.Debit-x.Credit)}).ToDictionaryAsync(x=>x.Id,x=>x.Amount,ct);
        var totals=all.GroupBy(x=>x.CustomerId).ToDictionary(g=>g.Key,g=>g.Sum(x=>Math.Max(0,x.Outstanding)));
        // Account promises stay separate. Document promises only affect their selected due.
        var history=await r.Db.Set<PaymentFollowUp>().Where(x=>x.BranchId==branch&&(x.SaleId!=null||x.ExistingDueId!=null)).OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).ToArrayAsync(ct);
        var byTarget=history.GroupBy(x=>x.ExistingDueId??x.SaleId!.Value).ToDictionary(g=>g.Key,g=>g.ToArray());
        return all.Select(x=>{
            var entries=byTarget.GetValueOrDefault(x.Id)??[];
            var promise=entries.FirstOrDefault(f=>f.Kind is "Promise" or "ClearPromise");
            var balance=balances.GetValueOrDefault(x.CustomerId);
            return x with{Outstanding=Math.Max(0,x.Outstanding),AccountBalance=balance,UnallocatedCredit=Math.Max(0,totals[x.CustomerId]-balance),PromiseDate=promise?.PromiseDate,NextContactDate=entries.FirstOrDefault()?.NextContactDate};
        }).ToArray();
    }
    private static CollectionRow[] Filter(CollectionRow[] rows,string? search,string? status,DateOnly? from,DateOnly? to,DateOnly today)
    {
        RetailOperations.Check(from is null||to is null||from<=to,"Start date must be on or before end date.");
        RetailOperations.Check(status is null or "" or "open" or "overdue" or "today" or "promised" or "settled" or "cancelled" or "all","Unknown collection status.");
        var q=rows.AsEnumerable();var term=(search??"").Trim();RetailOperations.Check(term.Length<=300,"Search must fit within 300 characters.");
        if(term.Length>0){var phone=new string(term.Where(char.IsAsciiDigit).ToArray());if(phone.Length==12&&phone.StartsWith("91",StringComparison.Ordinal))phone=phone[2..];
            q=q.Where(x=>new[]{x.CustomerName,x.Reference,x.Description,x.FinanceReference,x.Phone,x.AlternatePhone}.Any(v=>v.Contains(term,StringComparison.OrdinalIgnoreCase))||phone.Length>=3&&(x.Phone.Contains(phone,StringComparison.Ordinal)||x.AlternatePhone.Contains(phone,StringComparison.Ordinal)));}
        q=status switch{
            "all"=>q,"settled"=>q.Where(x=>!x.Cancelled&&x.Outstanding==0),"cancelled"=>q.Where(x=>x.Cancelled),
            "overdue"=>q.Where(x=>x.Outstanding>0&&(x.PromiseDate??x.DueDate)<today),
            "today"=>q.Where(x=>x.Outstanding>0&&((x.PromiseDate??x.DueDate)==today||x.NextContactDate<=today)),
            "promised"=>q.Where(x=>x.Outstanding>0&&x.PromiseDate!=null),_=>q.Where(x=>x.Outstanding>0)};
        if(from is not null)q=q.Where(x=>(x.PromiseDate??x.DueDate)>=from);
        if(to is not null)q=q.Where(x=>(x.PromiseDate??x.DueDate)<=to);
        return q.OrderBy(x=>x.PromiseDate??x.DueDate??DateOnly.MaxValue).ThenBy(x=>x.CustomerName).ThenBy(x=>x.Id).ToArray();
    }
    public async Task<object> ListAsync(Guid branch,int page,int size,string? search,string? status,DateOnly? from,DateOnly? to,CancellationToken ct)
    {
        RetailOperations.Page(page,size);
        await using var snapshot=await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var today=await r.TodayAsync(ct);var rows=Filter(await RowsAsync(branch,ct),search,status,from,to,today);
        var shop=await r.Db.Businesses.SingleAsync(ct);var settings=await r.SettingsAsync(ct);
        return new{items=rows.Skip((page-1)*size).Take(size),page,pageSize=size,totalItems=rows.Length,totalOutstanding=rows.Sum(x=>x.Outstanding),customers=rows.Select(x=>x.CustomerId).Distinct().Count(),unallocatedCredit=rows.GroupBy(x=>x.CustomerId).Sum(g=>g.First().UnallocatedCredit),today,shop=new{name=shop.TradeName,phone=settings.Phone}};
    }
    public async Task<byte[]> ExportAsync(Guid branch,string? search,string? status,DateOnly? from,DateOnly? to,CancellationToken ct)
    {
        await r.BranchAsync(branch,"reports.export",ct);
        await using var snapshot=await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var today=await r.TodayAsync(ct);var rows=Filter(await RowsAsync(branch,ct),search,status,from,to,today);
        string Amount(decimal value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
        string Date(DateOnly? value)=>value?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"";
        return ExportService.Rows(["As of","Customer","Phone","Second phone","Source","Reference","Device / reason","Finance reference (information only)","Original date","Due date","Promise date","Follow-up date","Amount after credit notes","Applied payments / exchange","Outstanding","Status","Overdue days","Unallocated customer credit (do not sum repeated customers)","Trading account balance (do not sum repeated customers)"],rows.Select(x=>new[]{Date(today),x.CustomerName,x.Phone,x.AlternatePhone,x.Kind,x.Reference,x.Description,x.FinanceReference,Date(x.BusinessDate),Date(x.DueDate),Date(x.PromiseDate),Date(x.NextContactDate),Amount(x.Amount),Amount(x.Paid),Amount(x.Outstanding),x.Cancelled?"Cancelled":x.Outstanding==0?"Settled":"Lena hai",x.Outstanding>0&&(x.PromiseDate??x.DueDate) is DateOnly due?Math.Max(0,today.DayNumber-due.DayNumber).ToString(CultureInfo.InvariantCulture):"0",Amount(x.UnallocatedCredit),Amount(x.AccountBalance)}));
    }
    public async Task<object> HistoryAsync(Guid branch,Guid id,string kind,CancellationToken ct)
    {
        await r.BranchAsync(branch,"customers.credit.view",ct);
        RetailOperations.Check(kind is "invoice" or "existing-due","Choose an invoice or register entry.");
        var exists=kind=="invoice"?await r.Db.Set<Sale>().AnyAsync(x=>x.Id==id&&x.BranchId==branch,ct):await r.Db.Set<ExistingDue>().AnyAsync(x=>x.Id==id&&x.BranchId==branch,ct);
        RetailOperations.Check(exists,"Entry not found in this branch.","NOT_FOUND");
        return new{items=await r.Db.Set<PaymentFollowUp>().Where(x=>x.BranchId==branch&&(kind=="invoice"?x.SaleId==id:x.ExistingDueId==id)).OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).Take(100).ToArrayAsync(ct)};
    }
    // Draft generation is read-only. It never creates a sent/delivered follow-up.
    public async Task<object> ReminderAsync(Guid branch,Guid customer,CancellationToken ct)
    {
        await using var snapshot=await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var rows=(await RowsAsync(branch,ct)).Where(x=>x.CustomerId==customer&&x.Outstanding>0).ToArray();
        RetailOperations.Check(rows.Length>0,"This customer has no unpaid invoice / register dues. Refresh the queue.");
        RetailOperations.Check(rows[0].UnallocatedCredit==0,"This customer has unallocated money on account. Allocate existing receipts before requesting payment, so the reminder cannot overstate the balance.");
        var settings=await r.SettingsAsync(ct);var business=await r.Db.Businesses.SingleAsync(ct);
        var total=rows.Sum(x=>x.Outstanding);var culture=CultureInfo.GetCultureInfo("en-IN");
        var lines=rows.Take(12).Select(x=>$"• {x.Reference}: ₹{x.Outstanding.ToString("N2",culture)}"+((x.PromiseDate??x.DueDate) is DateOnly date?$" · due {date.ToString("dd MMM yyyy",CultureInfo.InvariantCulture)}":""));
        var message=$"Hello {rows[0].CustomerName} Ji,\n\nThis is a payment reminder from {business.TradeName}. Your outstanding shop dues total ₹{total.ToString("N2",culture)}.\n\n{string.Join('\n',lines)}"+(rows.Length>12?$"\nPlus {rows.Length-12} other unpaid entries.":"")+"\n\nPlease pay as promised to keep your shop account in good standing and avoid being marked as a payment defaulter in our shop records. If you need more time, please call us to agree a new payment date.\n\nThank you,\n"+business.TradeName+(string.IsNullOrWhiteSpace(settings.Phone)?"":"\n"+settings.Phone);
        return new{customerId=customer,customerName=rows[0].CustomerName,phone=rows[0].Phone,alternatePhone=rows[0].AlternatePhone,amount=total,message,deliveryStatus="Draft only"};
    }
}
