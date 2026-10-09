using System.Globalization;
using System.Text.Json;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Exports;

public sealed class InventoryCsvExport(RetailOperations r,StockService stock)
{
    public async Task<byte[]> RenderAsync(Guid branch,string view,string? search,string? brand,string? category,string? status,CancellationToken ct)
    {
        await r.BranchAsync(branch,"inventory.view",ct);
        RetailOperations.Check(view is "devices" or "quantity" or "aging" or "movements","Choose a supported inventory export view.");
        var shop=await r.Db.Businesses.SingleAsync(ct);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone);
        var branchName=await r.Db.Branches.Where(x=>x.Id==branch).Select(x=>x.Name).SingleAsync(ct);
        string Date(DateTimeOffset? value)=>value is null?"":TimeZoneInfo.ConvertTime(value.Value,zone).ToString("yyyy-MM-dd HH:mm:ss zzz",CultureInfo.InvariantCulture);
        var exported=Date(DateTimeOffset.UtcNow);
        var cost=r.Can("inventory.cost.view");
        var rows=new List<string[]>();
        string[] headers=view=="movements"?["Exported at","Branch","Movement date","Product","SKU","Movement","Quantity change","Note"]:
            view=="quantity"?["Exported at","Branch","Product","Brand","Category","SKU","Barcode","On hand","Selling price","Last movement"]:
            ["Exported at","Branch","Product","Brand","Category","SKU","RAM","Storage","Colour","IMEI 1","IMEI 2","Serial / shop tag","Status","Condition","Received date","Days since receipt","Last movement","Selling price"];
        if(cost&&view is "devices" or "aging")headers=[..headers,"Unit cost"];
        for(var page=1;page<=1000;page++)
        {
            int count;
            if(view is "devices" or "aging")
            {
                var data=await stock.InventoryAsync(branch,page,100,search,ct,aging:view=="aging",brand:brand,category:category,status:status);
                count=data.Items.Count;var productIds=data.Items.Select(x=>x.ProductVariantId).ToArray();
                var products=await (from v in r.Db.Set<ProductVariant>() join m in r.Db.Set<ProductModel>() on v.ProductModelId equals m.Id where productIds.Contains(v.Id) select new{v.Id,m.Name,m.Brand,m.Category,v.Ram,v.Storage,v.Color}).ToDictionaryAsync(x=>x.Id,ct);
                var unitIds=data.Items.Select(x=>x.Id).ToArray();var last=await r.Db.Set<InventoryMovement>().Where(x=>x.BranchId==branch&&x.StockUnitId!=null&&unitIds.Contains(x.StockUnitId.Value)).GroupBy(x=>x.StockUnitId!.Value).Select(g=>new{Id=g.Key,At=g.Max(x=>x.CreatedAtUtc)}).ToDictionaryAsync(x=>x.Id,x=>x.At,ct);
                foreach(var unit in data.Items)
                {
                    var product=products[unit.ProductVariantId];string Identity(string slot)=>unit.Identifiers.FirstOrDefault(x=>x.StartsWith(slot+": ",StringComparison.Ordinal))?[(slot.Length+2)..]??"";
                    var serial=string.Join(" / ",unit.Identifiers.Where(x=>!x.StartsWith("IMEI",StringComparison.Ordinal)).Select(x=>x[(x.IndexOf(':')+2)..]));
                    string[] row=[exported,branchName,product.Name,product.Brand,product.Category,unit.Sku,product.Ram,product.Storage,product.Color,Identity("IMEI1"),Identity("IMEI2"),serial,unit.Status,unit.Condition,Date(unit.ReceivedAtUtc),Math.Max(0,(int)(DateTimeOffset.UtcNow-unit.ReceivedAtUtc).TotalDays).ToString(CultureInfo.InvariantCulture),Date(last.TryGetValue(unit.Id,out var moved)?moved:null),unit.SellingPrice.ToString("0.00",CultureInfo.InvariantCulture)];
                    if(cost)row=[..row,unit.Cost?.ToString("0.00",CultureInfo.InvariantCulture)??""];rows.Add(row);
                }
            }
            else if(view=="quantity")
            {
                var data=JsonSerializer.SerializeToElement(await stock.BalancesAsync(branch,ct,page,100,search,brand,category,status));var items=data.GetProperty("items").EnumerateArray().ToArray();count=items.Length;
                foreach(var item in items)
                {
                    var variant=item.GetProperty("ProductVariantId").GetGuid();var moved=await r.Db.Set<InventoryMovement>().Where(x=>x.BranchId==branch&&x.ProductVariantId==variant).MaxAsync(x=>(DateTimeOffset?)x.CreatedAtUtc,ct);
                    string Text(string name)=>item.TryGetProperty(name,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
                    rows.Add([exported,branchName,Text("name"),Text("Brand"),Text("Category"),Text("Sku"),Text("Barcode"),Text("Quantity"),Text("SellingPrice"),Date(moved)]);
                }
            }
            else
            {
                var movements=await (from entry in stock.MovementQuery(branch,null,search) join v in r.Db.Set<ProductVariant>() on entry.ProductVariantId equals v.Id join m in r.Db.Set<ProductModel>() on v.ProductModelId equals m.Id select new{entry.Id,entry.CreatedAtUtc,entry.Kind,entry.Quantity,entry.Note,v.Sku,Product=m.Brand+" "+m.Name}).OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*100).Take(100).ToArrayAsync(ct);count=movements.Length;
                foreach(var movement in movements)rows.Add([exported,branchName,Date(movement.CreatedAtUtc),movement.Product,movement.Sku,movement.Kind,movement.Quantity.ToString(CultureInfo.InvariantCulture),movement.Note]);
            }
            if(count<100)break;RetailOperations.Check(page<1000,"Export exceeds 100,000 rows. Narrow the inventory filters.");
        }
        return ExportService.Rows(headers,rows);
    }
}
