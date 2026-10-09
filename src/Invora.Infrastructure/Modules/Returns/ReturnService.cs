using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Modules.Taxes;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Payments;
using Invora.Infrastructure.Modules.Retail;
using Invora.Infrastructure.Modules.Sales;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Returns;
public sealed class ReturnService(RetailOperations r,SalesService sales,StockService stock,PaymentService payments)
{
    public Task<DocumentView> ReturnAsync(Guid saleId,ReturnRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("sale-return",key,new{saleId,request},async()=>{
        var sale=await sales.AuthorizedAsync(saleId,request.Cancel?"sales.cancel":"sales.return",ct);RetailOperations.Text(request.Reason,"Return reason");RetailOperations.Check(sale.Status is "Completed" or "PartiallyReturned","Sale cannot be returned.","INVALID_RETURN_STATE");RetailOperations.Check(request.Items is {Length:>0 and <=200} && request.Items.Select(x=>x.SaleItemId).Distinct().Count()==request.Items.Length,"Return needs unique item lines.");
        var originalSnapshot=RetailOperations.Read<InvoiceSnapshot>(sale.InvoiceJson);
        var original=await r.Db.Set<SaleItem>().Where(x=>x.SaleId==saleId).ToListAsync(ct);
        if(request.Cancel)RetailOperations.Check(sale.Status=="Completed" && request.Items.Length==original.Count && original.All(x=>request.Items.Any(y=>y.SaleItemId==x.Id && y.Quantity==x.Quantity)),"Cancellation must reverse the entire unreturned invoice.");
        var document=new SaleReturn{SaleId=saleId,Reason=request.Reason,BusinessDate=request.BusinessDate,Cancellation=request.Cancel,Number=await r.NumberAsync(sale.BranchId,"CRN",request.BusinessDate,ct)};r.Db.Add(document);var invoiceLines=new List<InvoiceLine>();
        foreach(var line in request.Items)
        {
            var item=original.SingleOrDefault(x=>x.Id==line.SaleItemId)??throw new DomainException("NOT_FOUND","Original sale item not found.");var previous=await r.Db.Set<SaleReturnItem>().Where(x=>x.SaleItemId==item.Id).ToListAsync(ct);var returned=previous.Sum(x=>x.Quantity);
            RetailOperations.Check(line.Quantity>0 && line.Quantity<=item.Quantity-returned,"Return exceeds remaining sold quantity.","EXCESS_RETURN");RetailOperations.Check(line.Disposition is "Restock" or "Damaged" or "Repair","Invalid return disposition.");
            var last=line.Quantity==item.Quantity-returned;decimal Part(decimal amount,Func<SaleReturnItem,decimal> selector)=>last?amount-previous.Sum(selector):InvoiceCalculator.Round(amount*line.Quantity/item.Quantity);
            var credit=Part(item.Total,x=>x.Credit);var taxable=Part(item.Taxable,x=>x.Taxable);var cgst=Part(item.Cgst,x=>x.Cgst);var sgst=Part(item.Sgst,x=>x.Sgst);var igst=Part(item.Igst,x=>x.Igst);var cess=credit-taxable-cgst-sgst-igst;
            var returnedCost=InvoiceCalculator.Round(item.Cost*line.Quantity/item.Quantity);
            if(item.StockUnitId is Guid unitId)
            {
                var unit=await r.Db.Set<StockUnit>().SingleAsync(x=>x.Id==unitId,ct);RetailOperations.Check(unit.Status==DeviceStatus.Sold && unit.ActiveSaleItemId==item.Id && unit.BranchId==sale.BranchId,"Device does not belong to the current sale lifecycle.","INVALID_RETURN");unit.Status=line.Disposition=="Restock"?DeviceStatus.InStock:line.Disposition=="Repair"?DeviceStatus.InRepair:DeviceStatus.Damaged;unit.ActiveSaleItemId=null;unit.Cost=item.Cost;r.Movement(sale.BranchId,item.ProductVariantId,line.Disposition=="Restock"?1:0,"SaleReturn",unitId,sale:saleId,saleReturn:document.Id,note:request.Reason);
            }
            else
            {
                var remaining=line.Quantity;returnedCost=0;
                foreach(var allocation in await r.Db.Set<SaleCostAllocation>().Where(x=>x.SaleItemId==item.Id).OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToListAsync(ct))
                {
                    var qty=Math.Min(remaining,allocation.Quantity-allocation.ReturnedQuantity);if(qty==0)continue;var layer=await r.Db.Set<StockCostLayer>().SingleAsync(x=>x.Id==allocation.StockCostLayerId,ct);allocation.ReturnedQuantity+=qty;remaining-=qty;
                    if(line.Disposition=="Restock"){layer.RemainingQuantity+=qty;allocation.RestoredQuantity+=qty;returnedCost+=qty*layer.UnitCost;}if(remaining==0)break;
                }
                RetailOperations.Check(remaining==0,"Return cost layers do not reconcile.","STOCK_RECONCILIATION_FAILED");
                if(line.Disposition=="Restock")(await stock.BalanceAsync(sale.BranchId,item.ProductVariantId,ct)).Quantity+=line.Quantity;
                r.Movement(sale.BranchId,item.ProductVariantId,line.Disposition=="Restock"?line.Quantity:0,line.Disposition=="Restock"?"SaleReturn":"ReturnWriteOff",sale:saleId,saleReturn:document.Id,note:request.Reason);
            }
            var returnItem=new SaleReturnItem{SaleReturnId=document.Id,SaleItemId=item.Id,Quantity=line.Quantity,Disposition=line.Disposition,Credit=credit,Cost=line.Disposition=="Restock"?returnedCost:0,Taxable=taxable,Cgst=cgst,Sgst=sgst,Igst=igst,Cess=cess};r.Db.Add(returnItem);document.Credit+=credit;invoiceLines.Add(new(item.Description,item.Hsn,RetailOperations.Read<string[]>(item.IdentifiersJson),line.Quantity,item.UnitPrice,InvoiceCalculator.Round(item.Discount*line.Quantity/item.Quantity),item.Rate,item.CessRate,taxable,cgst,sgst,igst,cess,credit,0,originalSnapshot.Lines.FirstOrDefault(x=>x.Description==item.Description&&x.Identifiers.SequenceEqual(RetailOperations.Read<string[]>(item.IdentifiersJson)))?.Condition??"",originalSnapshot.Lines.FirstOrDefault(x=>x.Description==item.Description&&x.Identifiers.SequenceEqual(RetailOperations.Read<string[]>(item.IdentifiersJson)))?.WarrantyText??""));
        }
        if(document.Credit>0)r.Ledger(sale.BranchId,sale.CustomerId,request.BusinessDate,"CreditNote",0,document.Credit,sale:saleId,saleReturn:document.Id,note:request.Reason);
        var snapshot=RetailOperations.Read<InvoiceSnapshot>(sale.InvoiceJson);document.InvoiceJson=RetailOperations.Json(snapshot with{Number=document.Number,Date=request.BusinessDate,Lines=invoiceLines.ToArray(),Taxable=invoiceLines.Sum(x=>x.Taxable),Cgst=invoiceLines.Sum(x=>x.Cgst),Sgst=invoiceLines.Sum(x=>x.Sgst),Igst=invoiceLines.Sum(x=>x.Igst),Cess=invoiceLines.Sum(x=>x.Cess),Total=document.Credit,DocumentTitle="CREDIT NOTE",Reference=await r.InvoiceReferenceAsync(sale.BranchId,"CRN",request.BusinessDate,document.Number,ct),IssuedAt=DateTimeOffset.UtcNow,OriginalInvoiceNumber=snapshot.Number,OriginalInvoiceDate=snapshot.Date});
        await r.Db.SaveChangesAsync(ct);var credits=await r.Db.Set<SaleReturn>().Where(x=>x.SaleId==saleId).SumAsync(x=>x.Credit,ct);await payments.ReleaseExcessAsync(saleId,sale.Total-credits,request.Reason,ct);
        var totalReturned=await r.Db.Set<SaleReturnItem>().Where(x=>original.Select(i=>i.Id).Contains(x.SaleItemId)).SumAsync(x=>x.Quantity,ct);sale.Status=request.Cancel?"Cancelled":totalReturned==original.Sum(x=>x.Quantity)?"Returned":"PartiallyReturned";r.Audit(request.Cancel?"SALE_CANCELLED":"SALE_RETURNED",saleId,new{document.Number,document.Credit,request.Reason});return new DocumentView(document.Id,document.Number,sale.Status,document.Credit);
    },ct);
    public async Task<InvoiceSnapshot> InvoiceAsync(Guid id,CancellationToken ct)
    {
        var note=await r.Db.Set<SaleReturn>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Credit note not found.");await sales.AuthorizedAsync(note.SaleId,"sales.view",ct);return RetailOperations.Read<InvoiceSnapshot>(note.InvoiceJson);
    }
}
