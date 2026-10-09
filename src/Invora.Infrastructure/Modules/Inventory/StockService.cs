using Invora.Contracts.Common;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Inventory;
public sealed class StockService(RetailOperations r,CatalogService catalog)
{
    public async Task ReceiveAsync(Guid branch,ProductVariant variant,int qty,decimal cost,DeviceCapture[]? captures,string source,Guid? purchase,Guid? purchaseItem,CancellationToken ct,decimal? totalCost=null)
    {
        RetailOperations.Check(qty is >0 and <=100000,"Invalid quantity.");RetailOperations.Money(cost);
        var exactCost=totalCost??cost*qty;RetailOperations.Money(exactCost);
        var baseCost=decimal.Floor(exactCost/qty*100)/100;
        var extra=(int)((exactCost-baseCost*qty)*100);
        if(variant.Serialized)
        {
            RetailOperations.Check(captures is not null && captures.Length==qty,"Capture exactly one identity set for every device.");
            var model=await r.Db.Set<ProductModel>().SingleAsync(x=>x.Id==variant.ProductModelId,ct);var requiresImei=await catalog.RequiresImeiAsync(model.Category,ct);
            var index=0;
            foreach(var capture in captures!)
            {
                RetailOperations.Check(!requiresImei||!string.IsNullOrWhiteSpace(capture.Imei1)||!string.IsNullOrWhiteSpace(capture.Imei2),"Each mobile phone needs its IMEI.");
                var unit=new StockUnit{BranchId=branch,ProductVariantId=variant.Id,Cost=baseCost+(index++<extra?0.01m:0),Source=source,PurchaseItemId=purchaseItem};r.Db.Add(unit);await IdentifyAsync(unit,capture,ct);r.Movement(branch,variant.Id,1,source,unit.Id,purchase:purchase);
            }
        }
        else
        {
            RetailOperations.Check(captures is null || captures.Length==0,"Quantity stock does not accept device identities.");
            var balance=await BalanceAsync(branch,variant.Id,ct);balance.Quantity+=qty;
            if(extra>0)r.Db.Add(new StockCostLayer{BranchId=branch,ProductVariantId=variant.Id,ReceivedQuantity=extra,RemainingQuantity=extra,UnitCost=baseCost+0.01m,PurchaseItemId=purchaseItem});
            if(qty>extra)r.Db.Add(new StockCostLayer{BranchId=branch,ProductVariantId=variant.Id,ReceivedQuantity=qty-extra,RemainingQuantity=qty-extra,UnitCost=baseCost,PurchaseItemId=purchaseItem});r.Movement(branch,variant.Id,qty,source,purchase:purchase);
        }
    }
    public static (string Kind,string Slot,string Value)[] Normalize(DeviceCapture capture,bool allowTrackingTag=false)
    {
        RetailOperations.Check(capture is not null,"Device identity is required.");var values=new List<(string,string,string)>();
        foreach(var item in new[]{("IMEI","IMEI1",capture!.Imei1),("IMEI","IMEI2",capture.Imei2),("SERIAL","SERIAL",capture.Serial)})
        {
            if(string.IsNullOrWhiteSpace(item.Item3))continue;var value=item.Item3.Trim().ToUpperInvariant();
            if(item.Item1=="IMEI")RetailOperations.Check(value.Length==15 && value.All(char.IsAsciiDigit),"IMEI must contain 15 ASCII digits.");else RetailOperations.Check(value.Length<=100,"Serial number is too long.");values.Add((item.Item1,item.Item2,value));
        }
        if(!string.IsNullOrWhiteSpace(capture.TrackingTag)){RetailOperations.Check(allowTrackingTag&&System.Text.RegularExpressions.Regex.IsMatch(capture.TrackingTag,@"^USED-[A-F0-9]{12}$"),"Shop tracking tags may only identify an existing old device for removal.");values.Add(("SHOP","SHOP",capture.TrackingTag));}
        RetailOperations.Check(values.Count>0,"IMEI or serial is required.");RetailOperations.Check(values.Select(x=>x.Item1+":"+x.Item3).Distinct().Count()==values.Count,"A device cannot repeat an identity.");return values.ToArray();
    }
    public async Task IdentifyAsync(StockUnit unit,DeviceCapture capture,CancellationToken ct)
    {
        foreach(var (kind,slot,value) in Normalize(capture))
        {
            RetailOperations.Check(!r.Db.Set<UnitIdentifier>().Local.Any(x=>x.Kind==kind && x.Value==value) && !await r.Db.Set<UnitIdentifier>().AnyAsync(x=>x.Kind==kind && x.Value==value,ct),"Device identity already exists.","DUPLICATE_IDENTIFIER");r.Db.Add(new UnitIdentifier{StockUnitId=unit.Id,Kind=kind,Slot=slot,Value=value});
        }
    }
    public async Task<StockBalance> BalanceAsync(Guid branch,Guid variant,CancellationToken ct)
    {
        var b=r.Db.Set<StockBalance>().Local.SingleOrDefault(x=>x.BranchId==branch && x.ProductVariantId==variant)??await r.Db.Set<StockBalance>().SingleOrDefaultAsync(x=>x.BranchId==branch && x.ProductVariantId==variant,ct);
        if(b is null){b=new(){BranchId=branch,ProductVariantId=variant};r.Db.Add(b);}return b;
    }
    public async Task<List<(StockCostLayer Layer,int Quantity)>> ConsumeAsync(Guid branch,Guid variant,int qty,CancellationToken ct)
    {
        var balance=await BalanceAsync(branch,variant,ct);RetailOperations.Check(qty>0 && balance.Quantity>=qty,"Insufficient stock.","INSUFFICIENT_STOCK");
        var layers=await r.Db.Set<StockCostLayer>().Where(x=>x.BranchId==branch && x.ProductVariantId==variant && x.RemainingQuantity>0).OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToListAsync(ct);var result=new List<(StockCostLayer,int)>();var remaining=qty;
        foreach(var layer in layers){var used=Math.Min(remaining,layer.RemainingQuantity);if(used==0)continue;layer.RemainingQuantity-=used;remaining-=used;result.Add((layer,used));if(remaining==0)break;}
        RetailOperations.Check(remaining==0,"Stock costs do not reconcile with quantity.","STOCK_RECONCILIATION_FAILED");balance.Quantity-=qty;return result;
    }
    public async Task<PageResponse<InventoryView>> InventoryAsync(Guid branch,int page,int size,string? search,CancellationToken ct,bool aging=false,Guid? productVariantId=null,bool availableOnly=false,Guid? productModelId=null,string? brand=null,string? category=null,string? status=null)
    {
        await r.BranchAsync(branch,"inventory.view",ct);RetailOperations.Page(page,size);var q=r.Db.Set<StockUnit>().AsNoTracking().Where(x=>x.BranchId==branch);
        if(!string.IsNullOrWhiteSpace(status)){RetailOperations.Check(Enum.TryParse<DeviceStatus>(status,true,out var selectedStatus)&&Enum.IsDefined(selectedStatus),"Choose a valid inventory status.");q=q.Where(x=>x.Status==selectedStatus);}
        if(aging || availableOnly)q=q.Where(x=>x.Status==DeviceStatus.InStock);
        if(productModelId is Guid modelId)q=q.Where(x=>r.Db.Set<ProductVariant>().Any(v=>v.Id==x.ProductVariantId&&v.ProductModelId==modelId));
        if(productVariantId is Guid productId)q=q.Where(x=>x.ProductVariantId==productId);
        if(!string.IsNullOrWhiteSpace(brand)){var selected=brand.Trim().ToUpperInvariant();q=q.Where(x=>r.Db.Set<ProductVariant>().Any(v=>v.Id==x.ProductVariantId&&r.Db.Set<ProductModel>().Any(m=>m.Id==v.ProductModelId&&m.Brand.ToUpper()==selected)));}
        if(!string.IsNullOrWhiteSpace(category)){var selected=category.Trim().ToUpperInvariant();var mobile=CatalogService.IsMobile(selected);q=q.Where(x=>r.Db.Set<ProductVariant>().Any(v=>v.Id==x.ProductVariantId&&r.Db.Set<ProductModel>().Any(m=>m.Id==v.ProductModelId&&(mobile?(new[]{"MOBILE","SMARTPHONE","MOBILE PHONE","PHONE"}.Contains(m.Category.ToUpper())||r.Db.Set<ProductCategory>().Any(c=>c.RequiresImei&&c.NormalizedName==m.Category.ToUpper())):m.Category.ToUpper()==selected))));}
        var parts=RetailOperations.SearchParts(search);
        foreach(var term in parts.Terms){var compact=term.Replace(" ","");q=q.Where(x=>r.Db.Set<UnitIdentifier>().Any(i=>i.StockUnitId==x.Id&&i.Value.Contains(compact))||r.Db.Set<ProductVariant>().Any(v=>v.Id==x.ProductVariantId&&r.Db.Set<ProductModel>().Any(m=>m.Id==v.ProductModelId&&(m.Brand+" "+m.Name+" "+m.Category+" "+v.Ram+" "+v.Storage+" "+v.Color+" "+v.Sku+" "+(v.Barcode??"")).ToUpper().Replace(" ","").Contains(compact))));}
        if(parts.Ram is not null)q=q.Where(x=>r.Db.Set<ProductVariant>().Any(v=>v.Id==x.ProductVariantId&&v.Ram.ToUpper().Replace(" ","").Replace("GB","")==parts.Ram&&v.Storage.ToUpper().Replace(" ","").Replace("GB","")==parts.Storage));
        var total=await q.LongCountAsync(ct);var units=await q.OrderByDescending(x=>x.ReceivedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct);var result=new List<InventoryView>();foreach(var unit in units)result.Add(await ViewAsync(unit,ct));return new(result,page,size,total);
    }
    public async Task<InventoryView> ViewAsync(StockUnit unit,CancellationToken ct)
    {
        var p=await catalog.ProductAsync(unit.ProductVariantId,ct);var ids=await r.Db.Set<UnitIdentifier>().Where(x=>x.StockUnitId==unit.Id).OrderBy(x=>x.Slot).Select(x=>x.Slot+": "+x.Value).ToArrayAsync(ct);return new(unit.Id,unit.ProductVariantId,unit.BranchId,CatalogService.Description(p.Model,p.Variant),p.Variant.Sku,unit.Condition,unit.Status.ToString(),ids,r.Can("inventory.cost.view")?unit.Cost:null,p.Variant.SellingPrice,unit.ReceivedAtUtc);
    }
    public async Task<object> ScanAsync(Guid branch,string value,CancellationToken ct)
    {
        await r.BranchAsync(branch,"inventory.view",ct);RetailOperations.Text(value,"Scan value",100);var code=value.Trim().ToUpperInvariant();
        var identities=await r.Db.Set<UnitIdentifier>().Where(x=>x.Value==code && r.Db.Set<StockUnit>().Any(u=>u.Id==x.StockUnitId && u.BranchId==branch)).Take(2).ToArrayAsync(ct);RetailOperations.Check(identities.Length<=1,"Scan matches multiple identity types. Use the inventory search.","AMBIGUOUS_SCAN");var identity=identities.SingleOrDefault();
        if(identity is not null){var unit=await r.Db.Set<StockUnit>().SingleOrDefaultAsync(x=>x.Id==identity.StockUnitId && x.BranchId==branch,ct)??throw new DomainException("NOT_FOUND","Device is not available in this branch.");return new{kind="unit",unit=await ViewAsync(unit,ct)};}
        var variant=await r.Db.Set<ProductVariant>().SingleOrDefaultAsync(x=>x.Barcode==value || x.Sku==code,ct)??throw new DomainException("NOT_FOUND","No matching stock or product.");return new{kind="product",productVariantId=variant.Id,serialized=variant.Serialized,price=variant.SellingPrice};
    }
    public async Task<object> BalancesAsync(Guid branch,CancellationToken ct,int page=1,int size=25,string? search=null,string? brand=null,string? category=null,string? status=null)
    {
        await r.BranchAsync(branch,"inventory.view",ct);RetailOperations.Page(page,size);var q=(from b in r.Db.Set<StockBalance>().AsNoTracking() join v in r.Db.Set<ProductVariant>() on b.ProductVariantId equals v.Id join m in r.Db.Set<ProductModel>() on v.ProductModelId equals m.Id where b.BranchId==branch select new{b.Id,b.ProductVariantId,b.Quantity,name=m.Brand+" "+m.Name,m.Brand,m.Category,v.Sku,v.Barcode,v.SellingPrice});
        if(!string.IsNullOrWhiteSpace(brand)){var value=brand.Trim().ToUpperInvariant();q=q.Where(x=>x.Brand.ToUpper()==value);}
        if(!string.IsNullOrWhiteSpace(category)){var value=category.Trim().ToUpperInvariant();q=q.Where(x=>x.Category.ToUpper()==value);}
        if(!string.IsNullOrWhiteSpace(status)){RetailOperations.Check(status is "InStock" or "OutOfStock","Choose available or out-of-stock for quantity inventory.");q=status=="InStock"?q.Where(x=>x.Quantity>0):q.Where(x=>x.Quantity==0);}
        foreach(var term in RetailOperations.SearchParts(search).Terms)q=q.Where(x=>x.name.ToUpper().Contains(term)||x.Sku.ToUpper().Contains(term)||(x.Barcode??"").ToUpper().Contains(term));return new{items=await q.OrderBy(x=>x.name).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct),page,pageSize=size,totalItems=await q.LongCountAsync(ct)};
    }
    public Task<Guid> CreateProductWithStockAsync(ProductWithStockRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("product-with-stock",key,request,async()=>{
        await r.BranchAsync(request.BranchId,"inventory.adjust",ct);await r.PermissionAsync("inventory.cost.view",ct);RetailOperations.Text(request.Reason,"Stock reason");
        var id=await catalog.CreateInternalAsync(CatalogService.SeedDeviceProduct(request.Product,request.DeviceStock),ct);await r.Db.SaveChangesAsync(ct);var product=await catalog.ProductAsync(id,ct);
        if(request.DeviceStock is not null){RetailOperations.Check(request.Devices is null && request.DeviceStock.Length==request.Quantity,"Device count must match quantity; use only device stock rows.");await ReceiveDeviceRowsAsync(request.BranchId,product.Variant,product.Model,request.DeviceStock,request.Reason,ct);}
        else await ReceiveAsync(request.BranchId,product.Variant,request.Quantity,request.UnitCost,request.Devices,"OpeningStock",null,null,ct);
        r.Audit("PRODUCT_OPENING_STOCK",id,new{request.BranchId,request.Quantity,request.UnitCost,request.Reason});return id;
    },ct);
    public Task<Guid> AddDeviceStockAsync(DeviceStockBatchRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("device-stock",key,request,async()=>{await r.BranchAsync(request.BranchId,"inventory.adjust",ct);await r.PermissionAsync("inventory.cost.view",ct);RetailOperations.Text(request.Reason,"Stock reason");var product=await catalog.ProductAsync(request.ProductVariantId,ct);await ReceiveDeviceRowsAsync(request.BranchId,product.Variant,product.Model,request.Devices,request.Reason,ct);var id=Guid.NewGuid();r.Audit("DEVICE_STOCK_ADDED",id,request);return id;},ct);
    private async Task ReceiveDeviceRowsAsync(Guid branch,ProductVariant basis,ProductModel model,DeviceStockLine[] rows,string reason,CancellationToken ct){
        RetailOperations.Check(rows is {Length:>0 and <=100} && rows.All(x=>x is not null&&x.Device is not null),"Capture 1–100 devices.");
        var requiresImei=await catalog.RequiresImeiAsync(model.Category,ct);foreach(var row in rows){RetailOperations.Check(!requiresImei||!string.IsNullOrWhiteSpace(row.Device.Imei1)||!string.IsNullOrWhiteSpace(row.Device.Imei2),"Each mobile phone needs its IMEI.");var variant=await catalog.DeviceVariantAsync(basis,model,row,ct);await r.Db.SaveChangesAsync(ct);await ReceiveAsync(branch,variant,1,row.UnitCost,[row.Device],"OpeningStock",null,null,ct);}
    }
    public Task<Guid> AdjustAsync(AdjustmentRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("stock-adjust",key,request,async()=>{
        await r.BranchAsync(request.BranchId,"inventory.adjust",ct);RetailOperations.Text(request.Reason,"Adjustment reason");RetailOperations.Check(request.Quantity!=0 && Math.Abs((long)request.Quantity)<=100000,"Invalid adjustment quantity.");var p=await catalog.ProductAsync(request.ProductVariantId,ct);
        if(request.Quantity>0)await ReceiveAsync(request.BranchId,p.Variant,request.Quantity,request.UnitCost,request.Devices,"OpeningStock",null,null,ct);
        else if(p.Variant.Serialized)
        {
            RetailOperations.Check(request.Devices is not null && request.Devices.Length == -request.Quantity,"Identify each device being written off.");
            foreach(var capture in request.Devices!){var identities=Normalize(capture,true);var first=identities[0];var id=await r.Db.Set<UnitIdentifier>().SingleOrDefaultAsync(x=>x.Kind==first.Kind && x.Value==first.Value,ct)??throw new DomainException("NOT_FOUND","Device not found.");var unit=await r.Db.Set<StockUnit>().SingleAsync(x=>x.Id==id.StockUnitId,ct);RetailOperations.Check(unit.BranchId==request.BranchId && unit.ProductVariantId==p.Variant.Id && unit.Status==DeviceStatus.InStock,"Device is unavailable.","INVENTORY_UNAVAILABLE");foreach(var identity in identities)RetailOperations.Check(await r.Db.Set<UnitIdentifier>().AnyAsync(x=>x.StockUnitId==unit.Id && x.Kind==identity.Kind && x.Value==identity.Value,ct),"Identity set does not belong to the selected device.");unit.Status=DeviceStatus.WrittenOff;r.Movement(request.BranchId,p.Variant.Id,-1,"WriteOff",unit.Id,note:request.Reason);}
        }
        else{await ConsumeAsync(request.BranchId,p.Variant.Id,-request.Quantity,ct);r.Movement(request.BranchId,p.Variant.Id,request.Quantity,"AdjustmentDecrease",note:request.Reason);}
        var idResult=Guid.NewGuid();r.Audit("STOCK_ADJUSTMENT",idResult,request);return idResult;
    },ct);
    public IQueryable<InventoryMovement> MovementQuery(Guid branch,Guid? unit,string? search)
    {
        var q=r.Db.Set<InventoryMovement>().AsNoTracking().Where(x=>x.BranchId==branch&&(unit==null||x.StockUnitId==unit));
        foreach(var term in RetailOperations.SearchParts(search).Terms)q=q.Where(x=>x.Kind.ToUpper().Contains(term)||x.Note.ToUpper().Contains(term)||r.Db.Set<ProductVariant>().Any(v=>v.Id==x.ProductVariantId&&(v.Sku.ToUpper().Contains(term)||r.Db.Set<ProductModel>().Any(m=>m.Id==v.ProductModelId&&(m.Brand+" "+m.Name).ToUpper().Contains(term))))||r.Db.Set<UnitIdentifier>().Any(i=>i.StockUnitId==x.StockUnitId&&i.Value.Contains(term)));
        return q;
    }
    public async Task<object> MovementsAsync(Guid branch,Guid? unit,int page,int size,CancellationToken ct,string? search=null)
    {
        await r.BranchAsync(branch,"inventory.view",ct);RetailOperations.Page(page,size);var q=MovementQuery(branch,unit,search);return new{items=await q.OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToArrayAsync(ct),totalItems=await q.LongCountAsync(ct),page,pageSize=size};
    }
}
