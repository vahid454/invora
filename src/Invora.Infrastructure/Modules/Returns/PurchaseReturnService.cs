using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Modules.Taxes;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Returns;
public sealed class PurchaseReturnService(RetailOperations r,StockService stock)
{
    public Task<Guid> ReturnAsync(Guid purchaseId,PurchaseReturnRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("purchase-return",key,new{purchaseId,request},async()=>{
        var purchase=await r.Db.Set<Purchase>().SingleOrDefaultAsync(x=>x.Id==purchaseId && x.Status=="Completed",ct)??throw new DomainException("NOT_FOUND","Completed purchase not found.");await r.BranchAsync(purchase.BranchId,"purchase.create",ct);await r.PermissionAsync("inventory.adjust",ct);RetailOperations.Text(request.Reason,"Reason");RetailOperations.Text(request.SupplierCreditReference,"Supplier credit-note reference",100);RetailOperations.Check(request.Items is {Length:>0 and <=200},"Return items are required.");
        var doc=new PurchaseReturn{PurchaseId=purchaseId,BusinessDate=request.BusinessDate,SupplierCreditReference=request.SupplierCreditReference,Reason=request.Reason};r.Db.Add(doc);
        foreach(var line in request.Items)
        {
            var item=await r.Db.Set<PurchaseItem>().SingleOrDefaultAsync(x=>x.Id==line.PurchaseItemId && x.PurchaseId==purchaseId,ct)??throw new DomainException("NOT_FOUND","Purchase item not found.");var previous=await r.Db.Set<PurchaseReturnItem>().Where(x=>x.PurchaseItemId==item.Id).ToListAsync(ct);var previousQty=previous.Sum(x=>x.Quantity)+r.Db.Set<PurchaseReturnItem>().Local.Where(x=>x.PurchaseItemId==item.Id && x.PurchaseReturnId==doc.Id).Sum(x=>x.Quantity);RetailOperations.Check(line.Quantity>0 && line.Quantity<=item.Quantity-previousQty,"Return exceeds purchased quantity.");var variant=await r.Db.Set<ProductVariant>().SingleAsync(x=>x.Id==item.ProductVariantId,ct);
            if(variant.Serialized){RetailOperations.Check(line.Quantity==1 && line.StockUnitId!=null,"Identify each device being returned.");var unit=await r.Db.Set<StockUnit>().SingleOrDefaultAsync(x=>x.Id==line.StockUnitId && x.BranchId==purchase.BranchId && x.PurchaseItemId==item.Id && x.Source=="Purchase",ct)??throw new DomainException("NOT_FOUND","Purchased device not found.");RetailOperations.Check(unit.Status==DeviceStatus.InStock,"Device is not available for supplier return.");unit.Status=DeviceStatus.WrittenOff;r.Movement(purchase.BranchId,item.ProductVariantId,-1,"PurchaseReturn",unit.Id,purchase:purchaseId,note:request.Reason);}
            else{var layers=await r.Db.Set<StockCostLayer>().Where(x=>x.PurchaseItemId==item.Id && x.BranchId==purchase.BranchId && x.RemainingQuantity>0).OrderBy(x=>x.CreatedAtUtc).ToListAsync(ct);RetailOperations.Check(layers.Sum(x=>x.RemainingQuantity)>=line.Quantity,"The original receipt stock is no longer available.");var remaining=line.Quantity;foreach(var layer in layers){var qty=Math.Min(remaining,layer.RemainingQuantity);layer.RemainingQuantity-=qty;remaining-=qty;if(remaining==0)break;}(await stock.BalanceAsync(purchase.BranchId,item.ProductVariantId,ct)).Quantity-=line.Quantity;r.Movement(purchase.BranchId,item.ProductVariantId,-line.Quantity,"PurchaseReturn",purchase:purchaseId,note:request.Reason);}
            var credit=line.Quantity==item.Quantity-previousQty?item.Total-previous.Sum(x=>x.Credit)-r.Db.Set<PurchaseReturnItem>().Local.Where(x=>x.PurchaseItemId==item.Id && x.PurchaseReturnId==doc.Id).Sum(x=>x.Credit):InvoiceCalculator.Round(item.Total*line.Quantity/item.Quantity);r.Db.Add(new PurchaseReturnItem{PurchaseReturnId=doc.Id,PurchaseItemId=item.Id,StockUnitId=line.StockUnitId,Quantity=line.Quantity,Credit=credit});doc.Credit+=credit;
        }
        if(doc.Credit>0)r.Db.Add(new LedgerEntry{BranchId=purchase.BranchId,PartyId=purchase.SupplierId,BusinessDate=request.BusinessDate,Kind="SupplierCreditNote",Debit=doc.Credit,PurchaseId=purchaseId,PurchaseReturnId=doc.Id,ActorId=r.Actor,Note=request.Reason});await r.Db.SaveChangesAsync(ct);
        var remainingValue=purchase.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==purchaseId).SumAsync(x=>x.Credit,ct);var excess=await r.AllocatedAsync(null,purchaseId,ct)-remainingValue;
        foreach(var a in await r.Db.Set<PaymentAllocation>().Where(x=>x.PurchaseId==purchaseId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct)){if(excess<=0)break;var active=a.Amount-await r.Db.Set<AllocationReversal>().Where(x=>x.PaymentAllocationId==a.Id).SumAsync(x=>x.Amount,ct);var release=Math.Min(excess,active);if(release>0){r.Db.Add(new AllocationReversal{PaymentAllocationId=a.Id,Amount=release,Reason=request.Reason,ActorId=r.Actor});excess-=release;}}
        r.Audit("PURCHASE_RETURNED",purchaseId,new{doc.Id,doc.Credit,request.SupplierCreditReference,request.Reason});return doc.Id;
    },ct);
}
