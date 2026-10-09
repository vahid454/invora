using Invora.Contracts.Common;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Payments;
public sealed class PaymentService(RetailOperations r)
{
    public Task<Guid> RecordLenDenAsync(LenDenRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("lenden-record",key,request,async()=>{
        await r.BranchAsync(request.BranchId,"ledger.adjust",ct);await r.PermissionAsync("payments.create",ct);await r.PermissionAsync("customers.credit.view",ct);
        await r.PartyAsync(request.CustomerId,PartyKind.Customer,ct);RetailOperations.Money(request.Amount,true);RetailOperations.Check(request.Direction is "In" or "Out","Choose money received or money given.");RetailOperations.Check(Enum.IsDefined(typeof(PaymentMethod),request.Method),"Unknown payment method.");RetailOperations.Text(request.Note,"Reason for LenDen");
        var payment=new Payment{BranchId=request.BranchId,PartyId=request.CustomerId,Amount=request.Amount,Direction=request.Direction,Method=(PaymentMethod)request.Method,BusinessDate=request.BusinessDate,Reference=request.Reference??"",Note=request.Note.Trim(),ActorId=r.Actor};await r.CaptureReceiptAsync(payment,ct);r.Db.Add(payment);
        var incoming=request.Direction=="In";r.Ledger(request.BranchId,request.CustomerId,request.BusinessDate,incoming?"LenDenReceived":"LenDenGiven",incoming?0:request.Amount,incoming?request.Amount:0,payment:payment.Id,note:payment.Note);r.Audit("LENDEN_RECORDED",payment.Id,new{request.CustomerId,request.Direction,request.Amount,request.Note});return payment.Id;
    },ct);
    public Task<Guid> RecordAsync(PaymentRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("payment-create",key,request,()=>ApplyAsync(request,ct),ct);
    public async Task<object> OptionsAsync(Guid branch,Guid partyId,CancellationToken ct)
    {
        await r.BranchAsync(branch,"payments.create",ct);var party=await r.Db.Set<Party>().SingleOrDefaultAsync(x=>x.Id==partyId&&x.IsActive,ct)??throw new Invora.Domain.Common.DomainException("NOT_FOUND","Account not found.");await r.PermissionAsync(party.Kind==PartyKind.Customer?"customers.view":"supplier.view",ct);var balance=await r.TradingBalanceAsync(branch,partyId,ct);var availableCredit=party.Kind==PartyKind.Customer?Math.Max(0,-balance):Math.Max(0,balance);var documents=new List<object>();
        if(party.Kind==PartyKind.Customer){await r.PermissionAsync("sales.view",ct);var invoices=await r.Db.Set<Sale>().Where(x=>x.BranchId==branch&&x.CustomerId==partyId&&x.Status!="Draft"&&x.Status!="Cancelled").OrderByDescending(x=>x.BusinessDate).ThenBy(x=>x.Id).ToArrayAsync(ct);foreach(var invoice in invoices){var due=await r.SaleDueAsync(invoice,ct);if(due>0)documents.Add(new{invoice.Id,invoice.Number,invoice.Status,invoice.Total,partyId,outstanding=due});}}
        else{await r.PermissionAsync("purchase.view",ct);var invoices=await r.Db.Set<Purchase>().Where(x=>x.BranchId==branch&&x.SupplierId==partyId&&x.Status=="Completed").OrderByDescending(x=>x.BusinessDate).ThenBy(x=>x.Id).ToArrayAsync(ct);foreach(var invoice in invoices){var due=Math.Max(0,invoice.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==invoice.Id).SumAsync(x=>x.Credit,ct)-await r.AllocatedAsync(null,invoice.Id,ct));if(due>0)documents.Add(new{invoice.Id,invoice.Number,invoice.Status,invoice.Total,partyId,outstanding=due});}}
        return new{availableCredit,documents};
    }
    public async Task<Guid> ApplyAsync(PaymentRequest request,CancellationToken ct)
    {
        await r.BranchAsync(request.BranchId,"payments.create",ct);RetailOperations.Money(request.Amount,true);RetailOperations.Check(Enum.IsDefined(typeof(PaymentMethod),request.Method),"Unknown payment method.");RetailOperations.Check(request.Allocations is not null && request.Allocations.Length<=200,"Allocations must be supplied.");
        var party=await r.Db.Set<Party>().SingleOrDefaultAsync(x=>x.Id==request.PartyId && x.IsActive,ct)??throw new DomainException("NOT_FOUND","Party not found.");
        await r.PermissionAsync(party.Kind==PartyKind.Customer?"customers.view":"supplier.view",ct);
        if(request.AutoAllocate)
        {
            RetailOperations.Check(!request.Refund&&request.InvoiceId is null&&request.Allocations!.Length==0,"Automatic allocation cannot be combined with an invoice, explicit allocations or a refund.");
            var allocations=new List<AllocationRequest>();var left=request.Amount;
            if(party.Kind==PartyKind.Customer)
            {
                var invoices=await r.Db.Set<Sale>().Where(x=>x.BranchId==request.BranchId&&x.CustomerId==party.Id&&x.Status!="Draft"&&x.Status!="Cancelled"&&x.Status!="Abandoned").OrderBy(x=>x.BusinessDate).ThenBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToArrayAsync(ct);
                foreach(var invoice in invoices){var applied=Math.Min(left,await r.SaleDueAsync(invoice,ct));if(applied>0){allocations.Add(new(invoice.Id,null,applied));left-=applied;}if(left==0||allocations.Count==200)break;}
            }
            else
            {
                var bills=await r.Db.Set<Purchase>().Where(x=>x.BranchId==request.BranchId&&x.SupplierId==party.Id&&x.Status=="Completed").OrderBy(x=>x.BusinessDate).ThenBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToArrayAsync(ct);
                foreach(var bill in bills){var due=Math.Max(0,bill.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==bill.Id).SumAsync(x=>x.Credit,ct)-await r.AllocatedAsync(null,bill.Id,ct));var applied=Math.Min(left,due);if(applied>0){allocations.Add(new(null,bill.Id,applied));left-=applied;}if(left==0||allocations.Count==200)break;}
            }
            request=request with{Allocations=allocations.ToArray()};
        }
        if(request.InvoiceId is Guid target)
        {
            RetailOperations.Check(!request.Refund&&request.Allocations!.Length==0,"Choose an invoice or explicit allocations, not both. Refunds do not allocate invoices.");
            decimal due;
            if(party.Kind==PartyKind.Customer)
            {
                var sale=await r.Db.Set<Sale>().SingleOrDefaultAsync(x=>x.Id==target&&x.BranchId==request.BranchId&&x.CustomerId==party.Id&&x.Status!="Draft"&&x.Status!="Cancelled"&&x.Status!="Abandoned",ct)??throw new DomainException("NOT_FOUND","Invoice not found in this customer's branch account.");
                due=await r.SaleDueAsync(sale,ct);
            }
            else
            {
                var purchase=await r.Db.Set<Purchase>().SingleOrDefaultAsync(x=>x.Id==target&&x.BranchId==request.BranchId&&x.SupplierId==party.Id&&x.Status=="Completed",ct)??throw new DomainException("NOT_FOUND","Bill not found in this supplier's branch account.");
                due=Math.Max(0,purchase.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==target).SumAsync(x=>x.Credit,ct)-await r.AllocatedAsync(null,target,ct));
            }
            var applied=Math.Min(request.Amount,due);
            request=request with{Allocations=applied>0?[new(party.Kind==PartyKind.Customer?target:null,party.Kind==PartyKind.Supplier?target:null,applied)]:[]};
        }
        var normalIn=party.Kind==PartyKind.Customer;var incoming=request.Refund?!normalIn:normalIn;
        if(request.Refund){var balance=await r.TradingBalanceAsync(request.BranchId,party.Id,ct);RetailOperations.Check(request.Amount<=(normalIn?-balance:balance) && request.Allocations!.Length==0,"Refund exceeds unallocated account credit.","REFUND_EXCEEDS_CREDIT");}
        var payment=new Payment{BranchId=request.BranchId,PartyId=party.Id,Amount=request.Amount,Direction=incoming?"In":"Out",Method=(PaymentMethod)request.Method,BusinessDate=request.BusinessDate,Reference=request.Reference??"",Note=request.Note??"",ActorId=r.Actor};await r.CaptureReceiptAsync(payment,ct);r.Db.Add(payment);
        r.Ledger(payment.BranchId,party.Id,payment.BusinessDate,request.Refund?"Refund":"Payment",incoming?0:payment.Amount,incoming?payment.Amount:0,payment:payment.Id,note:payment.Note);
        await r.Db.SaveChangesAsync(ct);
        foreach(var allocation in request.Allocations!)await AllocateInternalAsync(payment,allocation,ct);
        r.Audit(request.Refund?"REFUND_RECORDED":"PAYMENT_RECORDED",payment.Id,new{payment.Amount,payment.Direction,payment.Method,payment.PartyId,request.InvoiceId,Allocated=request.Allocations!.Sum(x=>x.Amount),OnAccount=payment.Amount-request.Allocations!.Sum(x=>x.Amount)});return payment.Id;
    }
    private async Task AllocateInternalAsync(Payment payment,AllocationRequest request,CancellationToken ct)
    {
        RetailOperations.Check(!await r.Db.Set<LedgerEntry>().AnyAsync(x=>x.PaymentId==payment.Id && x.Kind.StartsWith("LenDen"),ct),"Independent LenDen entries cannot be allocated to an invoice.","LENDEN_NOT_ALLOCATABLE");
        RetailOperations.Money(request.Amount,true);RetailOperations.Check((request.SaleId is null)!=(request.PurchaseId is null),"Choose exactly one invoice target.");
        RetailOperations.Check(payment.ReversesPaymentId is null && !await r.Db.Set<Payment>().AnyAsync(x=>x.ReversesPaymentId==payment.Id,ct),"Reversed payments cannot be allocated.","PAYMENT_REVERSED");
        var allocated=await r.Db.Set<PaymentAllocation>().Where(x=>x.PaymentId==payment.Id).SumAsync(x=>x.Amount-r.Db.Set<AllocationReversal>().Where(y=>y.PaymentAllocationId==x.Id).Sum(y=>(decimal?)y.Amount).GetValueOrDefault(),ct);
        RetailOperations.Check(allocated+request.Amount<=payment.Amount,"Payment over-allocation.","PAYMENT_OVERALLOCATED");
        var party=await r.Db.Set<Party>().SingleAsync(x=>x.Id==payment.PartyId,ct);
        decimal documentDues=0;
        if(party.Kind==PartyKind.Customer){foreach(var invoice in await r.Db.Set<Sale>().Where(x=>x.BranchId==payment.BranchId && x.CustomerId==payment.PartyId && x.Status!="Draft").ToArrayAsync(ct))documentDues+=await r.SaleDueAsync(invoice,ct);}
        else{foreach(var invoice in await r.Db.Set<Purchase>().Where(x=>x.BranchId==payment.BranchId && x.SupplierId==payment.PartyId && x.Status=="Completed").ToArrayAsync(ct))documentDues+=Math.Max(0,invoice.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==invoice.Id).SumAsync(x=>x.Credit,ct)-await r.AllocatedAsync(null,invoice.Id,ct));}
        var accountDebt=await r.TradingBalanceAsync(payment.BranchId,payment.PartyId,ct)*(party.Kind==PartyKind.Customer?1:-1);
        RetailOperations.Check(request.Amount<=documentDues-accountDebt,"Account credit has already been consumed or refunded.","PAYMENT_CREDIT_UNAVAILABLE");
        if(request.SaleId is Guid saleId){var sale=await r.Db.Set<Sale>().SingleOrDefaultAsync(x=>x.Id==saleId && x.CustomerId==payment.PartyId && x.BranchId==payment.BranchId && x.Status!="Draft" && x.Status!="Cancelled",ct)??throw new DomainException("NOT_FOUND","Sale not found.");RetailOperations.Check(payment.Direction=="In" && request.Amount<=await r.SaleDueAsync(sale,ct),"Allocation exceeds invoice due.","INVOICE_OVERALLOCATED");}
        else{var purchase=await r.Db.Set<Purchase>().SingleOrDefaultAsync(x=>x.Id==request.PurchaseId && x.SupplierId==payment.PartyId && x.BranchId==payment.BranchId && x.Status=="Completed",ct)??throw new DomainException("NOT_FOUND","Purchase not found.");RetailOperations.Check(payment.Direction=="Out" && request.Amount<=purchase.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==purchase.Id).SumAsync(x=>x.Credit,ct)-await r.AllocatedAsync(null,purchase.Id,ct),"Allocation exceeds purchase payable.","INVOICE_OVERALLOCATED");}
        r.Db.Add(new PaymentAllocation{PaymentId=payment.Id,SaleId=request.SaleId,PurchaseId=request.PurchaseId,Amount=request.Amount});await r.Db.SaveChangesAsync(ct);
    }
    public Task<Guid> AllocateAsync(Guid id,AllocationRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("payment-allocate",key,new{id,request},async()=>{
        var payment=await r.Db.Set<Payment>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Payment not found.");await r.BranchAsync(payment.BranchId,"payments.create",ct);await AllocateInternalAsync(payment,request,ct);r.Audit("PAYMENT_ALLOCATED",id,request);return id;
    },ct);
    public Task<Guid> ReverseAsync(Guid id,ReasonRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("payment-reverse",key,new{id,request},async()=>{
        var payment=await r.Db.Set<Payment>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Payment not found.");await r.BranchAsync(payment.BranchId,"payments.reverse",ct);RetailOperations.Text(request.Reason,"Reversal reason");RetailOperations.Check(payment.ReversesPaymentId is null && !await r.Db.Set<Payment>().AnyAsync(x=>x.ReversesPaymentId==id,ct),"Payment already reversed.","PAYMENT_REVERSED");
        var reverse=new Payment{BranchId=payment.BranchId,PartyId=payment.PartyId,Amount=payment.Amount,Direction=payment.Direction=="In"?"Out":"In",Method=payment.Method,BusinessDate=await r.TodayAsync(ct),ReversesPaymentId=id,Note=request.Reason,ActorId=r.Actor};await r.CaptureReceiptAsync(reverse,ct);r.Db.Add(reverse);
        var entry=await r.Db.Set<LedgerEntry>().SingleAsync(x=>x.PaymentId==id,ct);r.Ledger(entry.BranchId,entry.PartyId,reverse.BusinessDate,entry.Kind.StartsWith("LenDen")?"LenDenReversal":"PaymentReversal",entry.Credit,entry.Debit,payment:reverse.Id,reverse:entry.Id,note:request.Reason);
        foreach(var allocation in await r.Db.Set<PaymentAllocation>().Where(x=>x.PaymentId==id).ToListAsync(ct)){var used=await r.Db.Set<AllocationReversal>().Where(x=>x.PaymentAllocationId==allocation.Id).SumAsync(x=>x.Amount,ct);if(allocation.Amount>used)r.Db.Add(new AllocationReversal{PaymentAllocationId=allocation.Id,Amount=allocation.Amount-used,Reason=request.Reason,ActorId=r.Actor});}
        r.Audit("PAYMENT_REVERSED",id,new{ReversalId=reverse.Id,request.Reason});return reverse.Id;
    },ct);
    public async Task ReleaseExcessAsync(Guid saleId,decimal remainingInvoiceValue,string reason,CancellationToken ct)
    {
        var excess=await r.AllocatedAsync(saleId,null,ct)-remainingInvoiceValue;if(excess<=0)return;
        foreach(var a in await r.Db.Set<PaymentAllocation>().Where(x=>x.SaleId==saleId).OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).ToListAsync(ct))
        {
            var active=a.Amount-await r.Db.Set<AllocationReversal>().Where(x=>x.PaymentAllocationId==a.Id).SumAsync(x=>x.Amount,ct);var release=Math.Min(excess,active);if(release>0){r.Db.Add(new AllocationReversal{PaymentAllocationId=a.Id,Amount=release,Reason=reason,ActorId=r.Actor});excess-=release;}if(excess==0)break;
        }
        if(excess>0)foreach(var settlement in await r.Db.Set<SaleNonCashSettlement>().Where(x=>x.SaleId==saleId).ToListAsync(ct))
        {
            var active=settlement.Amount-await r.Db.Set<NonCashSettlementReversal>().Where(x=>x.SaleNonCashSettlementId==settlement.Id).SumAsync(x=>x.Amount,ct);var release=Math.Min(excess,active);if(release>0){r.Db.Add(new NonCashSettlementReversal{SaleNonCashSettlementId=settlement.Id,Amount=release,Reason=reason});excess-=release;}if(excess==0)break;
        }
    }
    public async Task<PaymentReceipt> ReceiptAsync(Guid id,CancellationToken ct){var payment=await r.Db.Set<Payment>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Payment not found.");await r.BranchAsync(payment.BranchId,"payments.view",ct);RetailOperations.Check(payment.ReceiptJson!="{}","This historical payment has no stored receipt.");return RetailOperations.Read<PaymentReceipt>(payment.ReceiptJson);}
    public async Task<object> ListAsync(Guid branch,int page,int size,Guid? partyId,CancellationToken ct,string? search=null)
    {
        await r.BranchAsync(branch,"payments.view",ct);RetailOperations.Page(page,size);var q=r.Db.Set<Payment>().AsNoTracking().Where(x=>x.BranchId==branch && (partyId==null || x.PartyId==partyId));if(!string.IsNullOrWhiteSpace(search)){var term=search.ToUpperInvariant();q=q.Where(x=>x.Reference.ToUpper().Contains(term)||x.Note.ToUpper().Contains(term)||r.Db.Set<Party>().Any(p=>p.Id==x.PartyId && (p.Name.ToUpper().Contains(term)||p.Phone.Contains(term))));}return new{items=await q.OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).Select(x=>new{x.Id,x.PartyId,partyName=r.Db.Set<Party>().Where(p=>p.Id==x.PartyId).Select(p=>p.Name).First(),phone=r.Db.Set<Party>().Where(p=>p.Id==x.PartyId).Select(p=>p.Phone).First(),purpose=r.Db.Set<LedgerEntry>().Where(e=>e.PaymentId==x.Id).Select(e=>e.Kind).FirstOrDefault(),x.Amount,x.Direction,x.Method,x.BusinessDate,x.Reference,x.Note,x.ReversesPaymentId,reversed=r.Db.Set<Payment>().Any(y=>y.ReversesPaymentId==x.Id)}).ToArrayAsync(ct),page,pageSize=size,totalItems=await q.LongCountAsync(ct)};
    }
}
