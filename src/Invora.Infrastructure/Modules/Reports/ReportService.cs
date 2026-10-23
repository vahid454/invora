using Invora.Contracts.Retail;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Customers;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Reports;
public sealed record RegisterOrigin(DateOnly Date,string Source,string Reference,string Description,decimal Outstanding);
public sealed record CustomerAccountRow(Guid Id,string Name,string Phone,string AlternatePhone,DateOnly? PromiseDate,DateOnly? NextContactDate,decimal TradeBalance,decimal IndependentBalance,string SafetyStatus)
{
    public string Address { get; init; } = "";
    public RegisterOrigin[] Origins { get; init; } = [];
    public DateOnly? FirstOriginalDate=>Origins.Length==0?null:Origins.Min(x=>x.Date);
    public decimal LenaHai=>Math.Max(0,TradeBalance)+Math.Max(0,IndependentBalance);
    public decimal DenaHai=>Math.Max(0,-TradeBalance)+Math.Max(0,-IndependentBalance);
    public DateOnly? NextDocumentDue { get; init; }
    public DateOnly? NextDocumentContact { get; init; }
    public decimal OpenDocumentTotal { get; init; }
    public int OpenDocumentCount { get; init; }
    public DateOnly? NextDueOrPromise=>new[]{PromiseDate,NextDocumentDue}.Where(x=>x.HasValue).Min();
    public DateOnly? NextFollowUp=>new[]{NextContactDate,NextDocumentContact}.Where(x=>x.HasValue).Min();
}
public sealed record CustomerRegisterReport(string ShopName,string BranchName,DateOnly AsOf,DateTimeOffset GeneratedAt,CustomerAccountRow[] Rows);
public sealed class ReportService(RetailOperations r,InventoryValuationService valuation,CollectionService collections)
{
    private sealed class AccountProjection
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string Phone { get; init; } = "";
        public string AlternatePhone { get; init; } = "";
        public string Address { get; init; } = "";
        public DateOnly? PromiseDate { get; init; }
        public DateOnly? NextContactDate { get; init; }
        public decimal TradeBalance { get; init; }
        public decimal IndependentBalance { get; init; }
    }
    private IQueryable<AccountProjection> AccountQuery(Guid branch,string? search,string? filter,DateOnly today)
    {
        var parties=r.Db.Set<Party>().AsNoTracking().Where(x=>x.Kind==PartyKind.Customer && x.IsActive);
        if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim().ToUpperInvariant();var digits=new string(term.Where(char.IsAsciiDigit).ToArray());var local=digits.Length>=10?digits[^10..]:digits;parties=parties.Where(x=>x.Name.ToUpper().Contains(term)||x.Phone.Contains(term)||x.AlternatePhone.Contains(term)||(digits.Length>=3 && (x.Phone.Contains(digits)||x.AlternatePhone.Contains(digits)))||(local.Length>=10 && (x.Phone.Contains(local)||x.AlternatePhone.Contains(local))));}
        var q=parties.Select(p=>new AccountProjection{Id=p.Id,Name=p.Name,Phone=p.Phone,AlternatePhone=p.AlternatePhone,Address=p.Address,PromiseDate=r.Db.Set<PaymentFollowUp>().Where(f=>f.CustomerId==p.Id&&f.BranchId==branch&&f.SaleId==null&&f.ExistingDueId==null&&(f.Kind=="Promise"||f.Kind=="ClearPromise")).OrderByDescending(f=>f.CreatedAtUtc).ThenByDescending(f=>f.Id).Select(f=>f.PromiseDate).FirstOrDefault(),NextContactDate=r.Db.Set<PaymentFollowUp>().Where(f=>f.CustomerId==p.Id&&f.BranchId==branch&&f.SaleId==null&&f.ExistingDueId==null).OrderByDescending(f=>f.CreatedAtUtc).ThenByDescending(f=>f.Id).Select(f=>f.NextContactDate).FirstOrDefault(),TradeBalance=r.Db.Set<LedgerEntry>().Where(e=>e.BranchId==branch && e.PartyId==p.Id && !e.Kind.StartsWith("LenDen")).Sum(e=>(decimal?)(e.Debit-e.Credit))??0,IndependentBalance=r.Db.Set<LedgerEntry>().Where(e=>e.BranchId==branch && e.PartyId==p.Id && e.Kind.StartsWith("LenDen")).Sum(e=>(decimal?)(e.Debit-e.Credit))??0});
        return filter switch{"owed"=>q.Where(x=>x.TradeBalance>0||x.IndependentBalance>0),"credit"=>q.Where(x=>x.TradeBalance<0||x.IndependentBalance<0),"trade"=>q.Where(x=>x.TradeBalance>0),"independent"=>q.Where(x=>x.IndependentBalance>0),"promised"=>q.Where(x=>x.PromiseDate!=null&&(x.TradeBalance>0||x.IndependentBalance>0)),"followup"=>q.Where(x=>x.NextContactDate<=today||x.PromiseDate<=today&&(x.TradeBalance>0||x.IndependentBalance>0)),"missed"=>q.Where(x=>x.PromiseDate<today&&(x.TradeBalance>0||x.IndependentBalance>0)),_=>q};
    }
    private async Task<CustomerAccountRow[]> AccountRowsAsync(AccountProjection[] items,CancellationToken ct)
    {
        var safety=await CustomerSafetyService.StatusesAsync(r.Db,items.Select(x=>x.Id),ct);
        return items.Select(x=>new CustomerAccountRow(x.Id,x.Name,x.Phone,x.AlternatePhone,x.PromiseDate,x.NextContactDate,x.TradeBalance,x.IndependentBalance,safety.GetValueOrDefault(x.Id,"Clear")){Address=x.Address}).ToArray();
    }
    public async Task<object> DashboardAsync(Guid branch,DateOnly? from,DateOnly? to,CancellationToken ct)
    {
        await r.BranchAsync(branch,"reports.view",ct);var business=await r.Db.Businesses.SingleAsync(ct);var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone)).Date);var start=from??today;var end=to??today;RetailOperations.Check(start<=end,"Invalid report period.");
        var sales=r.Db.Set<Sale>().Where(x=>x.BranchId==branch && x.Status!="Draft" && x.BusinessDate>=start && x.BusinessDate<=end);
        var notes=r.Db.Set<SaleReturn>().Where(x=>r.Db.Set<Sale>().Any(s=>s.Id==x.SaleId && s.BranchId==branch) && x.BusinessDate>=start && x.BusinessDate<=end);
        var creditIds=notes.Select(x=>x.Id);var returnedLines=r.Db.Set<SaleReturnItem>().Where(x=>creditIds.Contains(x.SaleReturnId));
        var gross=await sales.SumAsync(x=>x.Total,ct);var credits=await notes.SumAsync(x=>x.Credit,ct);var revenue=await sales.SumAsync(x=>x.Taxable,ct)-await returnedLines.SumAsync(x=>x.Taxable,ct);
        var cost=await sales.SumAsync(x=>x.Cost,ct)-await returnedLines.SumAsync(x=>x.Cost,ct);
        var payments=r.Db.Set<Payment>().Where(x=>x.BranchId==branch && x.BusinessDate>=start && x.BusinessDate<=end);
        var inAmount=await payments.Where(x=>x.Direction=="In").SumAsync(x=>x.Amount,ct);var outAmount=await payments.Where(x=>x.Direction=="Out").SumAsync(x=>x.Amount,ct);
        var expenses=await r.Db.Set<Expense>().Where(x=>x.BranchId==branch && x.BusinessDate>=start && x.BusinessDate<=end).SumAsync(x=>x.ReversesExpenseId==null?x.Amount:-x.Amount,ct);
        var balances=await (from e in r.Db.Set<LedgerEntry>() join p in r.Db.Set<Party>() on e.PartyId equals p.Id where e.BranchId==branch group new{e,p} by new{e.PartyId,p.Kind,Independent=e.Kind.StartsWith("LenDen")} into g select new{g.Key.Kind,g.Key.Independent,Amount=g.Sum(x=>x.e.Debit-x.e.Credit)}).ToArrayAsync(ct);
        decimal? stockValue=null,stockPurchaseValue=null;
        if(r.Can("inventory.cost.view"))
        {
            var units=await r.Db.Set<StockUnit>().AsNoTracking().Where(x=>x.BranchId==branch&&x.Status==DeviceStatus.InStock).ToArrayAsync(ct);
            var layers=await r.Db.Set<StockCostLayer>().AsNoTracking().Where(x=>x.BranchId==branch&&x.RemainingQuantity>0).ToArrayAsync(ct);
            stockValue=units.Sum(x=>x.Cost)+layers.Sum(x=>x.RemainingQuantity*x.UnitCost);
            stockPurchaseValue=(await valuation.UnitsAsync(units,ct)).Values.Sum()+(await valuation.QuantitiesAsync(layers,ct)).Values.Sum();
        }
        var methods=await payments.GroupBy(x=>x.Method).Select(g=>new{method=g.Key,received=g.Where(x=>x.Direction=="In").Sum(x=>x.Amount),paid=g.Where(x=>x.Direction=="Out").Sum(x=>x.Amount)}).ToArrayAsync(ct);
        var financeAssigned=await r.Db.Set<LedgerEntry>().Where(x=>x.BranchId==branch&&(x.Kind=="FinanceAssigned"||x.Kind=="FinanceReturnReversal")).SumAsync(x=>x.Credit-x.Debit,ct);
        return new{from=start,to=end,grossSales=gross,creditNotes=credits,netSales=gross-credits,revenue,paymentsReceived=inAmount,paymentsPaid=outAmount,cashFlow=inAmount-outAmount-expenses,expenses,customerOutstanding=balances.Where(x=>x.Kind==PartyKind.Customer && x.Amount>0).Sum(x=>x.Amount),customerCredit=balances.Where(x=>x.Kind==PartyKind.Customer && x.Amount<0).Sum(x=>-x.Amount),financeAssigned,supplierPayable=balances.Where(x=>x.Kind==PartyKind.Supplier && x.Amount<0).Sum(x=>-x.Amount),supplierReceivable=balances.Where(x=>x.Kind==PartyKind.Supplier && x.Amount>0).Sum(x=>x.Amount),stockValue,stockPurchaseValue,grossProfit=r.Can("profit.view")?(decimal?)(revenue-cost):null,estimatedOperatingProfit=r.Can("profit.view")?(decimal?)(revenue-cost-expenses):null,stockDevices=await r.Db.Set<StockUnit>().CountAsync(x=>x.BranchId==branch && x.Status==DeviceStatus.InStock,ct),methods};
    }
    public async Task<object> AccountsAsync(Guid branch,int page,int size,string? search,CancellationToken ct,string? filter=null)
    {
        await r.BranchAsync(branch,"customers.credit.view",ct);RetailOperations.Page(page,size);
        var q=AccountQuery(branch,search,filter,await r.TodayAsync(ct));
        var pageRows=await q.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct);return new{items=await AccountRowsAsync(pageRows,ct),page,pageSize=size,totalItems=await q.LongCountAsync(ct)};
    }
    public async Task<CustomerAccountRow> AccountAsync(Guid branch,Guid customerId,CancellationToken ct)
    {
        await r.BranchAsync(branch,"customers.credit.view",ct);
        var account=await AccountQuery(branch,null,null,await r.TodayAsync(ct)).SingleOrDefaultAsync(x=>x.Id==customerId,ct)
            ??throw new Invora.Domain.Common.DomainException("NOT_FOUND","Customer account not found.");
        return (await AccountRowsAsync([account],ct))[0];
    }
    public async Task<CustomerRegisterReport> CustomerRegisterReportAsync(Guid branch,string? search,string? filter,CancellationToken ct)
    {
        await r.BranchAsync(branch,"customers.credit.view",ct);await r.BranchAsync(branch,"reports.export",ct);
        await using var snapshot=await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var today=await r.TodayAsync(ct);var q=AccountQuery(branch,search,filter,today);
        RetailOperations.Check(await q.CountAsync(ct)<=20000,"Customer register exceeds 20,000 accounts. Narrow the customer search before exporting.");
        var accounts=await AccountRowsAsync(await q.OrderBy(x=>x.Name).ThenBy(x=>x.Id).ToArrayAsync(ct),ct);
        var documents=await collections.OpenRowsAsync(branch,ct);
        var grouped=documents.GroupBy(x=>x.CustomerId).ToDictionary(x=>x.Key,x=>x.ToArray());
        var accountIds=accounts.Select(x=>x.Id).ToArray();
        var openSaleIds=documents.Where(x=>x.Kind=="invoice"&&accountIds.Contains(x.CustomerId)).Select(x=>x.Id).ToArray();
        var saleDescriptions=await r.Db.Set<SaleItem>().AsNoTracking().Where(x=>openSaleIds.Contains(x.SaleId))
            .Select(x=>new{x.SaleId,x.Description}).ToArrayAsync(ct);
        var descriptions=saleDescriptions.GroupBy(x=>x.SaleId).ToDictionary(x=>x.Key,x=>string.Join("; ",x.Select(y=>y.Description).Distinct().Take(3)));
        var movements=await r.Db.Set<LedgerEntry>().AsNoTracking().Where(x=>x.BranchId==branch&&accountIds.Contains(x.PartyId)&&x.Kind.StartsWith("LenDen"))
            .OrderBy(x=>x.BusinessDate).ThenBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToArrayAsync(ct);
        var reversed=movements.Where(x=>x.Kind=="LenDenReversal"&&x.ReversesEntryId.HasValue).Select(x=>x.ReversesEntryId!.Value).ToHashSet();
        var active=movements.Where(x=>x.Kind is "LenDenGiven" or "LenDenReceived"&&!reversed.Contains(x.Id)).ToArray();
        var paymentIds=active.Where(x=>x.Kind=="LenDenGiven"&&x.PaymentId.HasValue).Select(x=>x.PaymentId!.Value).ToArray();
        var references=await r.Db.Set<Payment>().AsNoTracking().Where(x=>paymentIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Reference,ct);
        var independent=active.GroupBy(x=>x.PartyId).ToDictionary(x=>x.Key,x=>{
            var received=x.Where(e=>e.Kind=="LenDenReceived").Sum(e=>e.Credit);
            var origins=new List<RegisterOrigin>();
            foreach(var given in x.Where(e=>e.Kind=="LenDenGiven")){
                var offset=Math.Min(received,given.Debit);received-=offset;var open=given.Debit-offset;
                if(open>0)origins.Add(new RegisterOrigin(given.BusinessDate,"LenDen given",given.PaymentId is Guid id?references.GetValueOrDefault(id,""):"",given.Note,open));
            }
            return origins.ToArray();
        });
        var rows=accounts.Select(x=>{
            var items=grouped.GetValueOrDefault(x.Id)??[];
            var sources=items.Select(d=>new RegisterOrigin(d.BusinessDate,d.Kind=="invoice"?"Invoice":"Old due",d.Reference,d.Kind=="invoice"?descriptions.GetValueOrDefault(d.Id,""):d.Description,d.Outstanding))
                .Concat(independent.GetValueOrDefault(x.Id)??[]).OrderBy(d=>d.Date).ThenBy(d=>d.Source).ThenBy(d=>d.Reference).ToArray();
            return x with{Origins=sources,OpenDocumentCount=items.Length,OpenDocumentTotal=items.Sum(d=>d.Outstanding),NextDocumentDue=items.Select(d=>d.PromiseDate??d.DueDate).Where(d=>d.HasValue).Min(),NextDocumentContact=items.Select(d=>d.NextContactDate).Where(d=>d.HasValue).Min()};
        }).ToArray();
        var business=await r.Db.Businesses.AsNoTracking().SingleAsync(ct);var shopBranch=await r.Db.Branches.AsNoTracking().SingleAsync(x=>x.Id==branch,ct);
        return new CustomerRegisterReport(business.TradeName,shopBranch.Name,today,DateTimeOffset.UtcNow,rows);
    }
    public async Task<object> OutstandingAsync(Guid branch,bool overdue,int page,int size,CancellationToken ct,string? search=null)
    {
        await r.BranchAsync(branch,"customers.credit.view",ct);RetailOperations.Page(page,size);var b=await r.Db.Businesses.SingleAsync(ct);var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(b.TimeZone)).Date);
        var q=from s in r.Db.Set<Sale>().AsNoTracking() join c in r.Db.Set<Party>() on s.CustomerId equals c.Id where s.BranchId==branch && s.Status!="Draft" && s.Status!="Cancelled" let credits=r.Db.Set<SaleReturn>().Where(x=>x.SaleId==s.Id).Sum(x=>(decimal?)x.Credit)??0 let paid=r.Db.Set<PaymentAllocation>().Where(x=>x.SaleId==s.Id).Sum(x=>(decimal?)(x.Amount-(r.Db.Set<AllocationReversal>().Where(a=>a.PaymentAllocationId==x.Id).Sum(a=>(decimal?)a.Amount)??0)))??0 let noncash=r.Db.Set<SaleNonCashSettlement>().Where(x=>x.SaleId==s.Id).Sum(x=>(decimal?)(x.Amount-(r.Db.Set<NonCashSettlementReversal>().Where(y=>y.SaleNonCashSettlementId==x.Id).Sum(y=>(decimal?)y.Amount)??0)))??0 where s.Total-credits-paid-noncash>0 && (!overdue || s.DueDate<today) select new{s.Id,s.Number,s.Reference,s.CustomerId,customerName=c.Name,c.Phone,c.AlternatePhone,s.BusinessDate,s.DueDate,s.Total,credits,paid,outstanding=s.Total-credits-paid-noncash};
        if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim().ToUpperInvariant();var digits=new string(term.Where(char.IsAsciiDigit).ToArray());if(digits.Length>10)digits=digits[^10..];q=q.Where(x=>x.customerName.ToUpper().Contains(term)||x.Number.ToUpper().Contains(term)||x.Reference.ToUpper().Contains(term)||x.Phone.Contains(term)||x.AlternatePhone.Contains(term)||(digits.Length>=3&&(x.Phone.Contains(digits)||x.AlternatePhone.Contains(digits))));}
        var total=await q.LongCountAsync(ct);var rows=await q.OrderBy(x=>x.DueDate).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct);var safety=await Invora.Infrastructure.Modules.Customers.CustomerSafetyService.StatusesAsync(r.Db,rows.Select(x=>x.CustomerId),ct);var results=new List<object>();foreach(var row in rows){var products=await r.Db.Set<SaleItem>().Where(x=>x.SaleId==row.Id).Select(x=>new{x.Description,x.IdentifiersJson,x.Quantity}).ToArrayAsync(ct);var last=await r.Db.Set<PaymentAllocation>().Where(x=>x.SaleId==row.Id && x.Amount>(r.Db.Set<AllocationReversal>().Where(y=>y.PaymentAllocationId==x.Id).Sum(y=>(decimal?)y.Amount)??0)).Join(r.Db.Set<Payment>(),a=>a.PaymentId,p=>p.Id,(a,p)=>p).MaxAsync(x=>(DateOnly?)x.BusinessDate,ct);results.Add(new{row.Id,row.Number,row.Reference,row.CustomerId,row.customerName,row.Phone,row.AlternatePhone,row.BusinessDate,row.DueDate,row.Total,row.credits,row.paid,row.outstanding,overdueDays=row.DueDate is DateOnly due?Math.Max(0,today.DayNumber-due.DayNumber):0,lastPaymentDate=last,products,safetyStatus=safety.GetValueOrDefault(row.CustomerId,"Clear")});}
        return new{items=results,page,pageSize=size,totalItems=total,totalPages=(total+size-1)/size};
    }
    public async Task<object> BranchesAsync(DateOnly from,DateOnly to,CancellationToken ct){await r.PermissionAsync("reports.view",ct);var branches=await r.Db.Branches.Where(x=>x.IsActive && r.Db.StaffBranches.Any(b=>b.BranchId==x.Id && b.UserId==r.Actor)).OrderBy(x=>x.Name).ToArrayAsync(ct);var result=new List<object>();foreach(var branch in branches)result.Add(new{branch.Id,branch.Name,summary=await DashboardAsync(branch.Id,from,to,ct)});return result;}
    public async Task<object> GstAsync(Guid branch,DateOnly fromDate,DateOnly toDate,CancellationToken ct)
    {
        await r.BranchAsync(branch,"reports.view",ct);RetailOperations.Check(fromDate<=toDate,"Invalid period.");var ids=r.Db.Set<Sale>().Where(x=>x.BranchId==branch && x.Status!="Draft" && x.BusinessDate>=fromDate && x.BusinessDate<=toDate).Select(x=>x.Id);
        var groups=await r.Db.Set<SaleItem>().Where(x=>ids.Contains(x.SaleId)).GroupBy(x=>new{x.Hsn,x.Rate,x.CessRate}).Select(g=>new{g.Key.Hsn,g.Key.Rate,g.Key.CessRate,taxable=g.Sum(x=>x.Taxable),cgst=g.Sum(x=>x.Cgst),sgst=g.Sum(x=>x.Sgst),igst=g.Sum(x=>x.Igst),cess=g.Sum(x=>x.Cess)}).ToArrayAsync(ct);
        var returns=await (from i in r.Db.Set<SaleReturnItem>() join n in r.Db.Set<SaleReturn>() on i.SaleReturnId equals n.Id join s in r.Db.Set<Sale>() on n.SaleId equals s.Id join original in r.Db.Set<SaleItem>() on i.SaleItemId equals original.Id where s.BranchId==branch && n.BusinessDate>=fromDate && n.BusinessDate<=toDate group i by new{original.Hsn,original.Rate,original.CessRate} into g select new{g.Key.Hsn,g.Key.Rate,g.Key.CessRate,taxable=g.Sum(x=>x.Taxable),cgst=g.Sum(x=>x.Cgst),sgst=g.Sum(x=>x.Sgst),igst=g.Sum(x=>x.Igst),cess=g.Sum(x=>x.Cess)}).ToArrayAsync(ct);return new{sales=groups,creditNotes=returns,notice="Internal GST summary; does not file a government return."};
    }
    public async Task<object> OperationsAsync(Guid branch,DateOnly fromDate,DateOnly toDate,CancellationToken ct)
    {
        await r.BranchAsync(branch,"reports.view",ct);RetailOperations.Check(fromDate<=toDate&&toDate.DayNumber-fromDate.DayNumber<=366,"Choose a period of up to one year.");
        var sales=await r.Db.Set<Sale>().AsNoTracking().Where(x=>x.BranchId==branch&&x.Status!="Draft"&&x.BusinessDate>=fromDate&&x.BusinessDate<=toDate).Select(x=>new{x.Id,x.BusinessDate,x.Total}).ToArrayAsync(ct);
        var returns=await (from n in r.Db.Set<SaleReturn>() join s in r.Db.Set<Sale>() on n.SaleId equals s.Id where s.BranchId==branch&&n.BusinessDate>=fromDate&&n.BusinessDate<=toDate select new{n.Id,n.BusinessDate,n.Credit}).ToArrayAsync(ct);
        var daily=Enumerable.Range(0,toDate.DayNumber-fromDate.DayNumber+1).Select(i=>{var date=fromDate.AddDays(i);return new{date,invoices=sales.Count(s=>s.BusinessDate==date),sales=sales.Where(s=>s.BusinessDate==date).Sum(s=>s.Total),returns=returns.Where(n=>n.BusinessDate==date).Sum(n=>n.Credit),netSales=sales.Where(s=>s.BusinessDate==date).Sum(s=>s.Total)-returns.Where(n=>n.BusinessDate==date).Sum(n=>n.Credit)};}).ToArray();
        var ids=sales.Select(s=>s.Id).ToArray();var returnIds=returns.Select(n=>n.Id).ToArray();
        var sold=await r.Db.Set<SaleItem>().Where(x=>ids.Contains(x.SaleId)).GroupBy(x=>x.ProductVariantId).Select(g=>new{ProductVariantId=g.Key,Description=g.Select(x=>x.Description).First(),quantity=g.Sum(x=>x.Quantity),amount=g.Sum(x=>x.Total)}).ToArrayAsync(ct);
        var back=await (from item in r.Db.Set<SaleReturnItem>() join original in r.Db.Set<SaleItem>() on item.SaleItemId equals original.Id where returnIds.Contains(item.SaleReturnId) group item by original.ProductVariantId into g select new{id=g.Key,quantity=g.Sum(x=>x.Quantity),amount=g.Sum(x=>x.Credit),description=r.Db.Set<SaleItem>().Where(i=>i.ProductVariantId==g.Key).OrderByDescending(i=>i.CreatedAtUtc).Select(i=>i.Description).First()}).ToArrayAsync(ct);
        var topProducts=sold.Select(x=>x.ProductVariantId).Concat(back.Select(x=>x.id)).Distinct().Select(id=>{var s=sold.FirstOrDefault(x=>x.ProductVariantId==id);var b=back.FirstOrDefault(x=>x.id==id);return new{ProductVariantId=id,Description=s?.Description??b!.description,quantity=(s?.quantity??0)-(b?.quantity??0),netSales=(s?.amount??0)-(b?.amount??0)};}).OrderByDescending(s=>s.netSales).Take(10).ToArray();
        var today=await r.TodayAsync(ct);var zone=TimeZoneInfo.FindSystemTimeZoneById((await r.Db.Businesses.SingleAsync(ct)).TimeZone);var received=await r.Db.Set<StockUnit>().Where(x=>x.BranchId==branch&&x.Status==DeviceStatus.InStock).Select(x=>x.ReceivedAtUtc).ToArrayAsync(ct);var ages=received.Select(x=>today.DayNumber-DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x,zone).Date).DayNumber).ToArray();
        var aging=new[]{new{label="0–30 days",devices=ages.Count(x=>x<=30)},new{label="31–60 days",devices=ages.Count(x=>x>30&&x<=60)},new{label="61–90 days",devices=ages.Count(x=>x>60&&x<=90)},new{label="90+ days",devices=ages.Count(x=>x>90)}};
        var inTransit=await r.Db.Set<StockUnit>().CountAsync(x=>x.BranchId==branch&&x.Status==DeviceStatus.InTransit,ct);var quantityStock=await r.Db.Set<StockBalance>().Where(x=>x.BranchId==branch).SumAsync(x=>x.Quantity,ct);return new{daily,topProducts,aging,inTransit,quantityStock,availableDevices=received.Length};
    }
    public async Task<object> AuditAsync(int page,int size,CancellationToken ct){await r.PermissionAsync("audit.view",ct);RetailOperations.Page(page,size);var owner=await r.Db.StaffUsers.AnyAsync(x=>x.Id==r.Actor && x.IsOwner,ct);var branches=await r.Db.StaffBranches.Where(x=>x.UserId==r.Actor).Select(x=>x.BranchId).ToArrayAsync(ct);var detailsAllowed=r.Can("inventory.cost.view");var q=r.Db.AccessAudits.AsNoTracking().Where(x=>owner || x.BranchIds.Length>0 && x.BranchIds.All(b=>branches.Contains(b)));return new{items=await q.OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).Select(x=>new{x.Id,x.ActorId,x.Action,x.ResourceId,x.BranchIds,x.CreatedAtUtc,detailsJson=detailsAllowed?x.DetailsJson:"{}"}).ToArrayAsync(ct),page,pageSize=size,totalItems=await q.LongCountAsync(ct)};}
}
