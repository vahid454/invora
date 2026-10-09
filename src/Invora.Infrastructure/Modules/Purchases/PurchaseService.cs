using Invora.Contracts.Common;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Modules.Taxes;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Purchases;
public sealed class PurchaseService(RetailOperations r,CatalogService catalog,StockService stock)
{
    public async Task<object> QuoteAsync(PurchaseRequest request,CancellationToken ct){await r.BranchAsync(request.BranchId,"purchase.create",ct);await r.PartyAsync(request.SupplierId,PartyKind.Supplier,ct);RetailOperations.Check(request.Items is {Length:>0 and <=200},"Items are required.");await ValidateDevicesAsync(request,ct);var lines=new List<RetailTaxTotal>();foreach(var line in request.Items){RetailOperations.Money(line.UnitCost);RetailOperations.Money(line.Discount);var p=await catalog.ProductAsync(line.ProductVariantId,ct);lines.Add(RetailTaxCalculator.Calculate(line.Quantity,line.UnitCost,line.Discount,p.Tax.Rate,p.Tax.CessRate,line.TaxInclusive,false));}return new{taxable=lines.Sum(x=>x.Taxable),tax=lines.Sum(x=>x.Cgst+x.Sgst+x.Cess),total=lines.Sum(x=>x.Total)};}
    private async Task ValidateDevicesAsync(PurchaseRequest request,CancellationToken ct){
        var identities=new HashSet<string>();
        foreach(var line in request.Items){var product=await catalog.ProductAsync(line.ProductVariantId,ct);RetailOperations.Check(line.Quantity is >0 and <=100000,"Quantity must be a positive whole number.");
            if(!product.Variant.Serialized){RetailOperations.Check(line.Devices is null||line.Devices.Length==0,"Quantity products do not accept device identities.");continue;}
            RetailOperations.Check(line.Devices is not null&&line.Devices.Length==line.Quantity,"Enter an identity for every device before reviewing the purchase.");var mobile=await catalog.RequiresImeiAsync(product.Model.Category,ct);
            foreach(var device in line.Devices!){var normalized=StockService.Normalize(device);RetailOperations.Check(!mobile||normalized.Any(x=>x.Kind=="IMEI"),"Each phone needs a 15-digit IMEI.");foreach(var identity in normalized){RetailOperations.Check(identities.Add(identity.Kind+":"+identity.Value),"The purchase repeats a device identity.","DUPLICATE_IDENTIFIER");RetailOperations.Check(!await r.Db.Set<UnitIdentifier>().AnyAsync(x=>x.Kind==identity.Kind&&x.Value==identity.Value,ct),"Device identity already exists.","DUPLICATE_IDENTIFIER");}}
        }
    }
    public Task<DocumentView> DraftAsync(PurchaseRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("purchase-draft",key,request,async()=>{
        await r.BranchAsync(request.BranchId,"purchase.create",ct);await r.PartyAsync(request.SupplierId,PartyKind.Supplier,ct);RetailOperations.Text(request.SupplierInvoice,"Supplier invoice",100);RetailOperations.Check(request.Items is {Length:>0 and <=200},"Purchase needs 1–200 items.");
        var p=new Purchase{FinancialYear=await r.FiscalYearAsync(request.BusinessDate,ct),BranchId=request.BranchId,SupplierId=request.SupplierId,SupplierInvoice=request.SupplierInvoice.Trim().ToUpperInvariant(),BusinessDate=request.BusinessDate,RequestJson=RetailOperations.Json(request)};r.Db.Add(p);r.Audit("PURCHASE_DRAFT",p.Id);return new DocumentView(p.Id,p.Number,p.Status,p.Total);
    },ct);
    public Task<DocumentView> CompleteAsync(Guid id,string key,CancellationToken ct)=>r.ExecuteAsync("purchase-complete",key,new{id},async()=>{
        var p=await r.Db.Set<Purchase>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Purchase not found.");await r.BranchAsync(p.BranchId,"purchase.create",ct);if(p.Status=="Completed")return new DocumentView(p.Id,p.Number,p.Status,p.Total);RetailOperations.Check(p.Status=="Draft","Purchase cannot be posted.");
        RetailOperations.Check(!await r.Db.Set<Purchase>().AnyAsync(x=>x.Id!=p.Id && x.SupplierId==p.SupplierId && x.SupplierInvoice==p.SupplierInvoice && x.FinancialYear==p.FinancialYear && x.Status=="Completed",ct),"Supplier invoice already posted.","SUPPLIER_INVOICE_EXISTS");await r.PartyAsync(p.SupplierId,PartyKind.Supplier,ct);var request=RetailOperations.Read<PurchaseRequest>(p.RequestJson);var settings=await r.SettingsAsync(ct);
        p.Number=await r.NumberAsync(p.BranchId,"PUR",p.BusinessDate,ct);
        foreach(var line in request.Items)
        {
            RetailOperations.Money(line.UnitCost);RetailOperations.Money(line.Discount);var product=await catalog.ProductAsync(line.ProductVariantId,ct);var tax=RetailTaxCalculator.Calculate(line.Quantity,line.UnitCost,line.Discount,product.Tax.Rate,product.Tax.CessRate,line.TaxInclusive,false);
            var item=new PurchaseItem{PurchaseId=p.Id,ProductVariantId=product.Variant.Id,Quantity=line.Quantity,UnitCost=line.UnitCost,Taxable=tax.Taxable,Tax=tax.Cgst+tax.Sgst+tax.Igst+tax.Cess,Total=tax.Total};r.Db.Add(item);p.Total+=tax.Total;
            var cost=InvoiceCalculator.Round((settings.GstRegistered && !settings.CompositionDealer?tax.Taxable:tax.Total)/line.Quantity);
            await stock.ReceiveAsync(p.BranchId,product.Variant,line.Quantity,cost,line.Devices,"Purchase",p.Id,item.Id,ct,settings.GstRegistered && !settings.CompositionDealer?tax.Taxable:tax.Total);
        }
        RetailOperations.Money(p.Total,true);p.Status="Completed";r.Ledger(p.BranchId,p.SupplierId,p.BusinessDate,"Purchase",0,p.Total,purchase:p.Id);r.Audit("PURCHASE_COMPLETED",p.Id,new{p.Number,p.Total});return new DocumentView(p.Id,p.Number,p.Status,p.Total);
    },ct);
    public async Task<PageResponse<DocumentView>> ListAsync(Guid branch,int page,int size,CancellationToken ct,string? search=null)
    {
        await r.BranchAsync(branch,"purchase.view",ct);RetailOperations.Page(page,size);var q=r.Db.Set<Purchase>().AsNoTracking().Where(x=>x.BranchId==branch);if(!string.IsNullOrWhiteSpace(search)){var term=search.ToUpperInvariant();q=q.Where(x=>x.Number.Contains(term) || x.SupplierInvoice.Contains(term) || r.Db.Set<Party>().Any(p=>p.Id==x.SupplierId && (p.Name.ToUpper().Contains(term)||p.Phone.Contains(term))));}return new(await q.OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).Select(x=>new DocumentView(x.Id,x.Number,x.Status,x.Total,x.SupplierId)).ToArrayAsync(ct),page,size,await q.LongCountAsync(ct));
    }
    public async Task<object> DetailAsync(Guid id,CancellationToken ct)
    {
        var p=await r.Db.Set<Purchase>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new DomainException("NOT_FOUND","Purchase not found.");await r.BranchAsync(p.BranchId,"purchase.view",ct);await r.PermissionAsync("inventory.cost.view",ct);
        var rows=new List<object>();foreach(var item in await r.Db.Set<PurchaseItem>().AsNoTracking().Where(x=>x.PurchaseId==id).ToArrayAsync(ct)){
            var product=await catalog.ProductAsync(item.ProductVariantId,ct);var devices=new List<object>();foreach(var unit in await r.Db.Set<StockUnit>().Where(x=>x.PurchaseItemId==item.Id && x.BranchId==p.BranchId && x.Source=="Purchase" && x.Status==DeviceStatus.InStock).ToArrayAsync(ct))devices.Add(new{unit.Id,identifiers=await r.Db.Set<UnitIdentifier>().Where(x=>x.StockUnitId==unit.Id).Select(x=>x.Value).ToArrayAsync(ct)});
            rows.Add(new{item.Id,item.ProductVariantId,item.Quantity,item.UnitCost,item.Taxable,item.Tax,item.Total,description=CatalogService.Description(product.Model,product.Variant),serialized=product.Variant.Serialized,returnedQuantity=await r.Db.Set<PurchaseReturnItem>().Where(x=>x.PurchaseItemId==item.Id).SumAsync(x=>x.Quantity,ct),availableUnits=devices});
        }
        return new{purchase=p,items=rows,paid=await r.AllocatedAsync(null,id,ct),outstanding=Math.Max(0,p.Total-await r.Db.Set<PurchaseReturn>().Where(x=>x.PurchaseId==id).SumAsync(x=>x.Credit,ct)-await r.AllocatedAsync(null,id,ct))};
    }
}
