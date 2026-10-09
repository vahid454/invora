using System.Text;
using System.Text.Json;
using Invora.Infrastructure.Modules.Retail;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Customers;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Purchases;
using Invora.Infrastructure.Modules.Sales;
using Invora.Infrastructure.Modules.Payments;
using Invora.Infrastructure.Modules.Expenses;
using Invora.Infrastructure.Modules.Reports;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Common;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Exports;
public sealed class ExportService(RetailOperations r,CatalogService catalog,PartyService parties,StockService stock,PurchaseService purchases,SalesService sales,PaymentService payments,ExpenseService expenses,ReportService reports,InventoryCsvExport inventory)
{
    public async Task<byte[]> CsvAsync(string kind,Guid branch,CancellationToken ct,string? view=null,string? search=null,string? brand=null,string? category=null,string? status=null,DateOnly? from=null,DateOnly? to=null)
    {
        await r.PermissionAsync("reports.export",ct);await r.BranchAsync(branch,"reports.export",ct);await using var snapshot=await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        if(kind=="inventory")return await inventory.RenderAsync(branch,view??"devices",search,brand,category,status,ct);
        var rows=new List<JsonElement>();var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        for(var page=1;page<=1000;page++)
        {
            object data=kind switch{"catalog"=>await catalog.ProductsAsync(page,100,null,ct),"customers"=>await parties.ListAsync(PartyKind.Customer,page,100,null,ct),"suppliers"=>await parties.SupplierAccountsAsync(branch,page,100,null,null,ct),"inventory"=>await stock.InventoryAsync(branch,page,100,null,ct),"sales"=>await sales.ListAsync(branch,page,100,ct,search,from,to),"purchases"=>await purchases.ListAsync(branch,page,100,ct),"payments"=>await payments.ListAsync(branch,page,100,null,ct),"expenses"=>await expenses.ListAsync(branch,page,100,ct),"lenden"=>await reports.OutstandingAsync(branch,false,page,100,ct),_=>throw new DomainException("VALIDATION_FAILED","Unsupported export type.")};
            var json=JsonSerializer.SerializeToElement(data,data.GetType(),options);var items=json.GetProperty("items").EnumerateArray().ToArray();rows.AddRange(items);if(items.Length<100)break;RetailOperations.Check(page<1000,"Export exceeds 100,000 rows. Use a filtered reporting process.");
        }
        if(rows.Count==0)return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("No records\r\n")).ToArray();var headers=rows[0].EnumerateObject().Select(p=>p.Name).ToArray();var csv=new StringBuilder();csv.AppendLine(string.Join(',',headers.Select(Escape)));
        foreach(var row in rows)csv.AppendLine(string.Join(',',headers.Select(h=>Escape(row.TryGetProperty(h,out var v)?v.ValueKind==JsonValueKind.String?v.GetString()??"":v.ValueKind==JsonValueKind.Null?"":v.GetRawText():""))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
    internal static byte[] Rows(string[] headers,IEnumerable<string[]> rows){var csv=new StringBuilder();csv.Append(string.Join(',',headers.Select(Escape))).Append("\r\n");foreach(var row in rows)csv.Append(string.Join(',',row.Select(Escape))).Append("\r\n");return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();}
    private static string Escape(string value){var stripped=value.TrimStart();if(stripped.Length>0 && "=+-@".Contains(stripped[0]) || value.StartsWith('\t') || value.StartsWith('\r'))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
}
