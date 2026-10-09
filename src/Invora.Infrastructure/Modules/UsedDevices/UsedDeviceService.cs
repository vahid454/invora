using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.UsedDevices;
public sealed class UsedDeviceService(RetailOperations r,CatalogService catalog,StockService stock)
{
    public Task<Guid> AcquireAsync(AcquisitionRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("device-acquisition",key,request,()=>AcquireInternalAsync(request,ct),ct);
    public async Task<Guid> AcquireInternalAsync(AcquisitionRequest request,CancellationToken ct)
    {
        await r.BranchAsync(request.BranchId,"inventory.create",ct);await r.PermissionAsync("payments.create",ct);var seller=await r.PartyAsync(request.CustomerId,PartyKind.Customer,ct);RetailOperations.Money(request.Amount,true);RetailOperations.Check(request.Condition is "Used" or "Refurbished" or "OpenBox","Invalid condition.");RetailOperations.Check(request.BatteryHealth is null or >=0 and <=100 && request.WarrantyDays is >=0 and <=3650,"Invalid inspection values.");await ValidateAsync(request,ct);var variantId=request.ProductVariantId??await CreateOldProductAsync(request,ct);await r.Db.SaveChangesAsync(ct);var p=await catalog.ProductAsync(variantId,ct);RetailOperations.Check(p.Variant.Serialized,"Acquisition requires a serialized product.");
        var device=request.Device??new DeviceCapture(null);var ids=HasIdentity(device)?StockService.Normalize(device):[];var first=ids.FirstOrDefault();var existing=ids.Length==0?null:await r.Db.Set<UnitIdentifier>().SingleOrDefaultAsync(x=>x.Kind==first.Kind && x.Value==first.Value,ct);StockUnit unit;
        if(existing is null){unit=new(){BranchId=request.BranchId,ProductVariantId=variantId,Cost=request.Amount,Condition=request.Condition,Source=request.ExchangeSaleId==null?"CustomerPurchase":"Exchange"};r.Db.Add(unit);if(ids.Length>0)await stock.IdentifyAsync(unit,device,ct);else r.Db.Add(new UnitIdentifier{StockUnitId=unit.Id,Kind="SHOP",Slot="SHOP",Value="USED-"+unit.Id.ToString("N")[..12].ToUpperInvariant()});}
        else{unit=await r.Db.Set<StockUnit>().SingleAsync(x=>x.Id==existing.StockUnitId,ct);RetailOperations.Check(!r.Db.Set<SaleItem>().Local.Any(x=>x.StockUnitId==unit.Id && x.SaleId==request.ExchangeSaleId) && unit.Status==DeviceStatus.Sold && unit.BranchId==request.BranchId && unit.ProductVariantId==variantId,"Existing device cannot be reacquired in this state.","INVENTORY_UNAVAILABLE");foreach(var id in ids)RetailOperations.Check(await r.Db.Set<UnitIdentifier>().AnyAsync(x=>x.StockUnitId==unit.Id && x.Kind==id.Kind && x.Value==id.Value,ct),"Existing device identities do not match.");unit.Status=DeviceStatus.InStock;unit.ActiveSaleItemId=null;unit.Cost=request.Amount;unit.Condition=request.Condition;unit.Source=request.ExchangeSaleId==null?"CustomerPurchase":"Exchange";unit.ReceivedAtUtc=DateTimeOffset.UtcNow;}
        var acquisition=new DeviceAcquisition{BranchId=request.BranchId,CustomerId=request.CustomerId,StockUnitId=unit.Id,Amount=request.Amount,ExchangeSaleId=request.ExchangeSaleId};r.Db.Add(acquisition);var today=await r.TodayAsync(ct);
        r.Db.Add(new LedgerEntry{BranchId=request.BranchId,PartyId=request.CustomerId,BusinessDate=today,Kind=request.ExchangeSaleId==null?"DeviceAcquisition":"ExchangeCredit",Credit=request.Amount,DeviceAcquisitionId=acquisition.Id,ActorId=r.Actor,Note=request.Notes??""});
        if(request.ExchangeSaleId is Guid saleId)
        {
            var sale=await r.Db.Set<Sale>().SingleOrDefaultAsync(x=>x.Id==saleId && x.BranchId==request.BranchId && x.CustomerId==request.CustomerId && x.Status=="Completed",ct)??throw new DomainException("NOT_FOUND","Exchange sale not found.");RetailOperations.Check(request.Amount<=await r.SaleDueAsync(sale,ct),"Exchange exceeds remaining invoice due.");r.Db.Add(new SaleNonCashSettlement{SaleId=saleId,DeviceAcquisitionId=acquisition.Id,Amount=request.Amount});
        }
        else
        {
            RetailOperations.Check(Enum.IsDefined(typeof(PaymentMethod),request.Method),"Invalid payout method.");var payment=new Payment{BranchId=request.BranchId,PartyId=request.CustomerId,Amount=request.Amount,Direction="Out",Method=(PaymentMethod)request.Method,BusinessDate=today,Note="Used-device acquisition payout",ActorId=r.Actor};await r.CaptureReceiptAsync(payment,ct);r.Db.Add(payment);acquisition.PaymentId=payment.Id;r.Ledger(request.BranchId,request.CustomerId,today,"AcquisitionPayout",request.Amount,0,payment:payment.Id);
        }
        r.Db.Add(new DeviceInspection{StockUnitId=unit.Id,SellerName=string.IsNullOrWhiteSpace(request.SellerName)?seller.Name:request.SellerName.Trim(),AadhaarLastFour=(request.AadhaarLastFour??"").Trim(),Notes=request.Notes??"",BatteryHealth=request.BatteryHealth,WarrantyDays=0});r.Movement(request.BranchId,unit.ProductVariantId,1,unit.Source,unit.Id,note:request.Notes??"");r.Audit("DEVICE_ACQUIRED",acquisition.Id,new{unit.Id,request.Amount,request.Condition,request.ExchangeSaleId});return acquisition.Id;
    }
    private static bool HasIdentity(DeviceCapture d)=>!string.IsNullOrWhiteSpace(d.Imei1)||!string.IsNullOrWhiteSpace(d.Imei2)||!string.IsNullOrWhiteSpace(d.Serial);
    public async Task ValidateAsync(AcquisitionRequest request,CancellationToken ct)
    {
        await r.BranchAsync(request.BranchId,"inventory.create",ct);await r.PermissionAsync("payments.create",ct);await r.PartyAsync(request.CustomerId,PartyKind.Customer,ct);
        RetailOperations.Money(request.Amount,true);RetailOperations.Check(request.Condition is "Used" or "Refurbished" or "OpenBox","Choose the old device condition.");RetailOperations.Check(request.BatteryHealth is null or >=0 and <=100 && request.WarrantyDays is >=0 and <=3650,"Check the battery health and warranty days.");
        RetailOperations.Check(string.IsNullOrEmpty(request.AadhaarLastFour)||System.Text.RegularExpressions.Regex.IsMatch(request.AadhaarLastFour,@"^\d{4}$"),"Enter only the last four Aadhaar digits, or leave this optional field blank.");RetailOperations.Check((request.SellerName?.Length??0)<=200,"Seller name is too long.");
        if(request.ProductVariantId is Guid id){var p=await catalog.ProductAsync(id,ct);RetailOperations.Check(p.Variant.Serialized,"Old phones need individual stock tracking.");}
        else {RetailOperations.Check(request.OldDevice is not null,"Enter the customer's old device name.");RetailOperations.Text(request.OldDevice!.Name,"Old device name",200);RetailOperations.Check((request.OldDevice.Brand?.Length??0)<=100&&(request.OldDevice.Ram?.Length??0)<=50&&(request.OldDevice.Storage?.Length??0)<=50&&(request.OldDevice.Color?.Length??0)<=100,"Old device details are too long.");}
        if(request.Device is not null&&HasIdentity(request.Device))_ = StockService.Normalize(request.Device);
    }
    private async Task<Guid> CreateOldProductAsync(AcquisitionRequest request,CancellationToken ct)
    {
        var old=request.OldDevice!;var tax=await r.Db.Set<TaxRate>().Where(x=>x.IsActive).OrderByDescending(x=>x.Rate==18&&x.CessRate==0).ThenBy(x=>x.Rate).FirstOrDefaultAsync(ct)??throw new DomainException("VALIDATION_FAILED","Configure a tax rate before receiving an old device.");
        return await catalog.CreateInternalAsync(new(old.Name,string.IsNullOrWhiteSpace(old.Brand)?"Unspecified":old.Brand,"Mobile","85171300",tax.Id,"USED-"+Guid.NewGuid().ToString("N")[..12],null,old.Ram??"",old.Storage??"",old.Color??"",true,request.Amount,request.Amount,0),ct);
    }
    public async Task<object> ListAsync(Guid branch,CancellationToken ct)
    {
        await r.BranchAsync(branch,"inventory.view",ct);var canCost=r.Can("inventory.cost.view");return await (from a in r.Db.Set<DeviceAcquisition>().AsNoTracking() join u in r.Db.Set<StockUnit>() on a.StockUnitId equals u.Id join c in r.Db.Set<Party>() on a.CustomerId equals c.Id join v in r.Db.Set<ProductVariant>() on u.ProductVariantId equals v.Id join m in r.Db.Set<ProductModel>() on v.ProductModelId equals m.Id where a.BranchId==branch orderby a.CreatedAtUtc descending select new{a.Id,a.StockUnitId,a.ExchangeSaleId,currentBranchId=u.BranchId,description=m.Brand+" "+m.Name+" "+v.Ram+" "+v.Storage+" "+v.Color,identifiers=r.Db.Set<UnitIdentifier>().Where(x=>x.StockUnitId==u.Id).OrderBy(x=>x.Slot).Select(x=>x.Slot+": "+x.Value).ToArray(),customer=c.Name,u.Condition,u.Status,amount=canCost?(decimal?)a.Amount:null,inspection=r.Db.Set<DeviceInspection>().Where(x=>x.StockUnitId==u.Id).OrderByDescending(x=>x.CreatedAtUtc).Select(x=>new{x.Notes,x.BatteryHealth,x.WarrantyDays}).FirstOrDefault()}).Take(100).ToArrayAsync(ct);
    }
}
