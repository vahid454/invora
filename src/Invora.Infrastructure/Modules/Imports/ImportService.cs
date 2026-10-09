using Invora.Infrastructure.Modules.Inventory;
using System.Globalization;
using System.Text;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Common;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Customers;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Imports;
public sealed class ImportService(RetailOperations r,CatalogService catalog,PartyService parties,StockService stock)
{
    private static string Permission(string kind)=>kind switch{"opening-stock"=>"inventory.adjust","catalog"=>"inventory.create","customers"=>"customers.manage","suppliers"=>"supplier.manage",_=>throw new DomainException("VALIDATION_FAILED","Unsupported import type.")};
    public Task<object> ValidateAsync(ImportRequest request,string key,CancellationToken ct)=>r.ExecuteAsync<object>("import-validate",key,request,async()=>{
        await r.PermissionAsync(Permission(request.Kind),ct);if(request.Kind=="opening-stock"){RetailOperations.Check(request.BranchId!=null,"Choose a branch for opening stock.");await r.BranchAsync(request.BranchId!.Value,"inventory.adjust",ct);await r.PermissionAsync("inventory.cost.view",ct);}RetailOperations.Check(request.Csv.Length is >0 and <=1000000,"CSV must fit within 1 MB.");var rows=Parse(request.Csv);RetailOperations.Check(rows.Count is >1 and <=1001,"CSV must have a header and 1–1000 data rows.");var header=rows[0].Select(x=>x.Trim().TrimStart('\uFEFF')).ToArray();RetailOperations.Check(header.Distinct().Count()==header.Length,"CSV header repeats a column.");var errors=new List<object>();var records=new List<object>();var seen=new HashSet<string>();
        for(var i=1;i<rows.Count;i++)
        {
            try{RetailOperations.Check(rows[i].Length==header.Length,"Column count does not match header.");var row=header.Zip(rows[i]).ToDictionary(x=>x.First,x=>x.Second);string Get(string k,string fallback="")=>row.GetValueOrDefault(k,fallback).Trim();decimal Money(string k)=>decimal.Parse(Get(k,"0"),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture);
                if(request.Kind!="opening-stock")RetailOperations.Text(Get("name"),"Name",200);
                if(request.Kind=="opening-stock"){
                    var sku=Get("sku").ToUpperInvariant();var variant=await r.Db.Set<ProductVariant>().SingleOrDefaultAsync(x=>x.Sku==sku && x.IsActive,ct);RetailOperations.Check(variant is not null,"SKU must match an active product.");var quantity=int.Parse(Get("quantity"),CultureInfo.InvariantCulture);RetailOperations.Check(quantity is >0 and <=100000,"Opening quantity must be positive and within 100,000.");var cost=Money("unitCost");RetailOperations.Money(cost);RetailOperations.Text(Get("reason"),"Opening stock reason");DeviceCapture[]? devices=null;if(variant!.Serialized){RetailOperations.Check(quantity==1,"Use one row per serialized device.");var device=new DeviceCapture(Get("imei1"),Get("imei2"),Get("serial"));foreach(var identity in StockService.Normalize(device))RetailOperations.Check(seen.Add(identity.Kind+":"+identity.Value) && !await r.Db.Set<UnitIdentifier>().AnyAsync(x=>x.Kind==identity.Kind && x.Value==identity.Value,ct),"Device identity already exists or repeats in this file.");devices=[device];}records.Add(new AdjustmentRequest(request.BranchId!.Value,variant.Id,quantity,cost,Get("reason"),devices));
                }else if(request.Kind=="catalog"){
                    var taxName=Get("taxName");var taxes=await r.Db.Set<TaxRate>().Where(x=>x.Name==taxName && x.IsActive).Take(2).ToArrayAsync(ct);RetailOperations.Check(taxes.Length==1,"Tax name must match one active configured rate.");var sku=Get("sku").ToUpperInvariant();RetailOperations.Text(sku,"SKU",100);RetailOperations.Check(seen.Add(sku) && !await r.Db.Set<ProductVariant>().AnyAsync(x=>x.Sku==sku,ct),"SKU already exists or repeats in this file.");RetailOperations.Text(Get("brand"),"Brand",100);RetailOperations.Text(Get("category"),"Category",100);RetailOperations.Text(Get("hsn"),"HSN",20);var price=Money("sellingPrice");var mrp=Money("mrp");RetailOperations.Money(price);RetailOperations.Money(mrp);var warranty=int.Parse(Get("warrantyMonths","12"),CultureInfo.InvariantCulture);RetailOperations.Check(warranty is >=0 and <=120,"Invalid warranty months.");RetailOperations.Check(bool.TryParse(Get("serialized","true"),out var serialized),"Serialized must be true or false.");records.Add(new ProductRequest(Get("name"),Get("brand"),Get("category"),Get("hsn"),taxes[0].Id,sku,Get("barcode") is ""?null:Get("barcode"),Get("ram"),Get("storage"),Get("color"),serialized,price,mrp,warranty));
                }else{var phone=new string(Get("phone").Where(char.IsAsciiDigit).ToArray());RetailOperations.Check(phone.Length is >=7 and <=15,"Phone must contain 7–15 digits.");RetailOperations.Check(seen.Add(phone),"Phone repeats in this file.");decimal? limit=Get("creditLimit")==""?null:Money("creditLimit");if(limit is not null)RetailOperations.Money(limit.Value);records.Add(new PartyRequest(Get("name"),phone,Get("email"),Get("address"),Get("stateCode"),Get("gstin"),limit));}
            }catch(Exception ex)when(ex is DomainException or FormatException or OverflowException){errors.Add(new{row=i+1,message=ex.Message});}
        }
        var job=new ImportJob{BranchId=request.Kind=="opening-stock"?request.BranchId:null,ActorId=r.Actor,Kind=request.Kind,RowsJson=RetailOperations.Json(records),ErrorsJson=RetailOperations.Json(errors),RowCount=rows.Count-1,Status=errors.Count==0?"Validated":"Invalid"};r.Db.Add(job);r.Audit("IMPORT_VALIDATED",job.Id,new{job.Kind,job.RowCount,errorCount=errors.Count});return new{job.Id,job.Status,job.RowCount,validRows=records.Count,errors,preview=records.Take(10)};
    },ct);
    public Task<object> CommitAsync(Guid id,string key,CancellationToken ct)=>r.ExecuteAsync<object>("import-commit",key,new{id},async()=>{
        var job=await r.Db.Set<ImportJob>().SingleOrDefaultAsync(x=>x.Id==id && x.ActorId==r.Actor,ct)??throw new DomainException("NOT_FOUND","Import not found.");await r.PermissionAsync(Permission(job.Kind),ct);RetailOperations.Check(job.Status=="Validated","Only validated, uncommitted imports may be committed.");RetailOperations.Check(job.CreatedAtUtc>DateTimeOffset.UtcNow.AddDays(-1),"Import preview expired. Validate again.");
        if(job.Kind=="opening-stock"){await r.BranchAsync(job.BranchId!.Value,"inventory.adjust",ct);await r.PermissionAsync("inventory.cost.view",ct);foreach(var row in RetailOperations.Read<AdjustmentRequest[]>(job.RowsJson)){var p=await catalog.ProductAsync(row.ProductVariantId,ct);await stock.ReceiveAsync(job.BranchId.Value,p.Variant,row.Quantity,row.UnitCost,row.Devices,"OpeningStock",null,null,ct);r.Audit("OPENING_STOCK_IMPORTED",job.Id,new{row.ProductVariantId,row.Quantity,row.Reason});}}
        else if(job.Kind=="catalog")foreach(var row in RetailOperations.Read<ProductRequest[]>(job.RowsJson))await catalog.CreateInternalAsync(row,ct);
        else foreach(var row in RetailOperations.Read<PartyRequest[]>(job.RowsJson))await parties.CreateInternalAsync(job.Kind=="customers"?PartyKind.Customer:PartyKind.Supplier,row,ct);
        job.Status="Committed";r.Audit("IMPORT_COMMITTED",job.Id,new{job.Kind,job.RowCount});return new{job.Id,job.Status,job.RowCount};
    },ct);
    public static List<string[]> Parse(string csv)
    {
        var rows=new List<string[]>();var cells=new List<string>();var cell=new StringBuilder();var quoted=false;var closed=false;
        for(var i=0;i<csv.Length;i++){var ch=csv[i];if(quoted){if(ch=='"'){if(i+1<csv.Length && csv[i+1]=='"'){cell.Append('"');i++;}else{quoted=false;closed=true;}}else cell.Append(ch);continue;}if(ch=='"'){RetailOperations.Check(cell.Length==0 && !closed,"Unexpected CSV quote.");quoted=true;}else if(ch==','){cells.Add(cell.ToString());cell.Clear();closed=false;}else if(ch is '\r' or '\n'){if(ch=='\r' && i+1<csv.Length && csv[i+1]=='\n')i++;cells.Add(cell.ToString());if(cells.Any(x=>x.Length>0))rows.Add(cells.ToArray());cells.Clear();cell.Clear();closed=false;}else{RetailOperations.Check(!closed,"Unexpected text after CSV quoted field.");cell.Append(ch);}}
        RetailOperations.Check(!quoted,"CSV has an unclosed quoted field.");if(cell.Length>0 || cells.Count>0){cells.Add(cell.ToString());rows.Add(cells.ToArray());}return rows;
    }
}
