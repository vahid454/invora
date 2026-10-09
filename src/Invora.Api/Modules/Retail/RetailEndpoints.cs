using Invora.Infrastructure.Modules.Exports;
using Invora.Infrastructure.Modules.Licensing;
using Invora.Infrastructure.Modules.Files;
using Invora.Infrastructure.Modules.Imports;
using Invora.Infrastructure.Modules.UsedDevices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Invora.Application.Abstractions;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Businesses;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Customers;
using Invora.Infrastructure.Modules.Expenses;
using Invora.Infrastructure.Modules.Inventory;
using Invora.Infrastructure.Modules.Payments;
using Invora.Infrastructure.Modules.Purchases;
using Invora.Infrastructure.Modules.Reports;
using Invora.Infrastructure.Modules.Retail;
using Invora.Infrastructure.Modules.Returns;
using Invora.Infrastructure.Modules.Sales;
using Invora.Infrastructure.Modules.StockTransfers;
using Invora.Infrastructure.Pdf;
namespace Invora.Api.Modules.Retail;
public static class RetailEndpoints
{
    private static string Key(HttpContext c)=>c.Request.Headers["Idempotency-Key"].ToString();
    public static void AddRetail(this IServiceCollection services)
    {
        services.AddScoped<DeviceCorrectionService>();services.AddScoped<LicenseService>();services.AddSingleton(TimeProvider.System);services.AddScoped<ExportService>();services.AddScoped<InventoryCsvExport>();services.AddScoped<DocumentService>();services.AddScoped<ImportService>();services.AddScoped<UsedDeviceService>();services.AddScoped<PurchaseReturnService>();services.AddScoped<RetailOperations>();services.AddScoped<CatalogService>();services.AddScoped<PartyService>();services.AddScoped<FollowUpService>();services.AddScoped<StockService>();services.AddScoped<PurchaseService>();services.AddScoped<PaymentService>();services.AddScoped<SalesService>();services.AddScoped<ReturnService>();services.AddScoped<TransferService>();services.AddScoped<ExpenseService>();services.AddScoped<ReportService>();services.AddScoped<SettingsService>();services.AddScoped<InvoicePdfService>();services.AddScoped<IInvoicePdfService>(sp=>sp.GetRequiredService<InvoicePdfService>());
        services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.Converters.Add(new DecimalStringConverter()));
    }
    public static void MapRetail(this WebApplication app)
    {
        var g=app.MapGroup("/api/v1").RequireAuthorization().AddEndpointFilter<RetailRequestFilter>();
        g.MapGet("/license",async(LicenseService s,CancellationToken ct)=>Results.Ok(await s.StatusAsync(ct)));
        g.MapPost("/license/activate",async(ActivateLicenseRequest req,HttpContext c,LicenseService s,CancellationToken ct)=>{await s.ActivateAsync(req,Key(c),ct);return Results.Ok(await s.StatusAsync(ct));});
        g.MapGet("/gst-states",()=>Results.Ok(GstSupply.States.Select(x=>new{code=x.Key,name=x.Value})));
        g.MapGet("/billing-context",async(RetailOperations r,CancellationToken ct)=>{var settings=await r.SettingsAsync(ct);return Results.Ok(new{settings.StateCode,settings.GstRegistered,settings.CompositionDealer});});
        g.MapGet("/categories",async(CatalogService s,CancellationToken ct)=>Results.Ok(await s.CategoriesAsync(ct)));
        g.MapPost("/categories",async(CategoryRequest req,HttpContext c,CatalogService s,CancellationToken ct)=>Results.Ok(await s.CreateCategoryAsync(req,Key(c),ct)));
        g.MapGet("/brands",async(CatalogService s,CancellationToken ct)=>Results.Ok(await s.BrandsAsync(ct)));
        g.MapPost("/brands",async(BrandRequest req,HttpContext c,CatalogService s,CancellationToken ct)=>Results.Ok(await s.CreateBrandAsync(req,Key(c),ct)));
        g.MapGet("/tax-rates",async(CatalogService s,CancellationToken ct)=>Results.Ok(await s.TaxRatesAsync(ct)));
        g.MapPost("/tax-rates",async(TaxRequest req,HttpContext c,CatalogService s,CancellationToken ct)=>Results.Ok(await s.CreateTaxAsync(req,Key(c),ct)));
        g.MapGet("/exports/{kind}",async(string kind,Guid branchId,ExportService s,CancellationToken ct,string? view=null,string? search=null,string? brand=null,string? category=null,string? status=null,DateOnly? from=null,DateOnly? to=null)=>Results.File(await s.CsvAsync(kind,branchId,ct,view,search,brand,category,status,from,to),"text/csv; charset=utf-8","invora-"+kind+"-"+DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")+".csv"));
        g.MapGet("/products",async(CatalogService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null,string? category=null,string? brand=null)=>Results.Ok(await s.ProductsAsync(page,pageSize,search,ct,category,brand)));
        g.MapGet("/products/{id:guid}",async(Guid id,CatalogService s,CancellationToken ct)=>Results.Ok(await s.ViewAsync(id,ct)));
        g.MapPost("/imports/validate",async(ImportRequest req,HttpContext c,ImportService s,CancellationToken ct)=>Results.Ok(await s.ValidateAsync(req,Key(c),ct)));
        g.MapPost("/imports/{id:guid}/commit",async(Guid id,HttpContext c,ImportService s,CancellationToken ct)=>Results.Ok(await s.CommitAsync(id,Key(c),ct)));
        g.MapPut("/products/{id:guid}",async(Guid id,ProductRequest req,HttpContext c,CatalogService s,CancellationToken ct)=>Results.Ok(await s.UpdateAsync(id,req,Key(c),ct)));
        g.MapPost("/products",async(ProductRequest req,HttpContext c,CatalogService s,CancellationToken ct)=>Results.Ok(await s.CreateProductAsync(req,Key(c),ct)));
        g.MapPost("/products/for-purchase",async(ProductPurchaseRequest req,HttpContext c,CatalogService s,CancellationToken ct)=>Results.Ok(await s.PreparePurchaseAsync(req,Key(c),ct)));
        g.MapPost("/products/with-stock",async(ProductWithStockRequest req,HttpContext c,StockService s,CancellationToken ct)=>Results.Ok(await s.CreateProductWithStockAsync(req,Key(c),ct)));
        g.MapGet("/suppliers/accounts",async(Guid branchId,PartyService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null,string? filter=null)=>Results.Ok(await s.SupplierAccountsAsync(branchId,page,pageSize,search,filter,ct)));
        MapParties(g,"customers",PartyKind.Customer);MapParties(g,"suppliers",PartyKind.Supplier);
        g.MapGet("/inventory/{id:guid}/details",async(Guid id,Guid branchId,DeviceCorrectionService s,CancellationToken ct)=>Results.Ok(await s.DetailAsync(id,branchId,ct)));
        g.MapPost("/inventory/{id:guid}/corrections",async(Guid id,DeviceCorrectionRequest req,HttpContext c,DeviceCorrectionService s,CancellationToken ct)=>Results.Ok(await s.CorrectAsync(id,req,Key(c),ct)));
        g.MapGet("/inventory",async(Guid branchId,StockService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null,bool aging=false,Guid? productVariantId=null,bool availableOnly=false,Guid? productModelId=null,string? brand=null,string? category=null,string? status=null)=>Results.Ok(await s.InventoryAsync(branchId,page,pageSize,search,ct,aging,productVariantId,availableOnly,productModelId,brand,category,status)));
        g.MapGet("/inventory/balances",async(Guid branchId,StockService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null,string? brand=null,string? category=null,string? status=null)=>Results.Ok(await s.BalancesAsync(branchId,ct,page,pageSize,search,brand,category,status)));
        g.MapGet("/inventory/scan",async(Guid branchId,string value,StockService s,CancellationToken ct)=>Results.Ok(await s.ScanAsync(branchId,value,ct)));
        g.MapGet("/inventory/movements",async(Guid branchId,StockService s,CancellationToken ct,Guid? unitId=null,int page=1,int pageSize=25,string? search=null)=>Results.Ok(await s.MovementsAsync(branchId,unitId,page,pageSize,ct,search)));
        g.MapPost("/inventory/devices",async(DeviceStockBatchRequest req,HttpContext c,StockService s,CancellationToken ct)=>Results.Ok(await s.AddDeviceStockAsync(req,Key(c),ct)));
        g.MapPost("/inventory/adjustments",async(AdjustmentRequest req,HttpContext c,StockService s,CancellationToken ct)=>Results.Ok(await s.AdjustAsync(req,Key(c),ct)));
        g.MapGet("/purchases",async(Guid branchId,PurchaseService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null)=>Results.Ok(await s.ListAsync(branchId,page,pageSize,ct,search)));
        g.MapPost("/purchases/quote",async(PurchaseRequest req,PurchaseService s,CancellationToken ct)=>Results.Ok(await s.QuoteAsync(req,ct)));
        g.MapPost("/purchases",async(PurchaseRequest req,HttpContext c,PurchaseService s,CancellationToken ct)=>Results.Ok(await s.DraftAsync(req,Key(c),ct)));
        g.MapGet("/purchases/{id:guid}",async(Guid id,PurchaseService s,CancellationToken ct)=>Results.Ok(await s.DetailAsync(id,ct)));
        g.MapPost("/purchases/{id:guid}/complete",async(Guid id,HttpContext c,PurchaseService s,CancellationToken ct)=>Results.Ok(await s.CompleteAsync(id,Key(c),ct)));
        g.MapGet("/sales",async(Guid branchId,SalesService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null,DateOnly? from=null,DateOnly? to=null)=>Results.Ok(await s.ListAsync(branchId,page,pageSize,ct,search,from,to)));
        g.MapPost("/sales/quote",async(SaleRequest req,SalesService s,CancellationToken ct)=>Results.Ok(await s.QuoteAsync(req,ct)));
        g.MapPost("/sales",async(SaleRequest req,HttpContext c,SalesService s,CancellationToken ct)=>Results.Ok(await s.DraftAsync(req,Key(c),ct)));
        g.MapGet("/sales/{id:guid}",async(Guid id,SalesService s,CancellationToken ct)=>Results.Ok(await s.DetailAsync(id,ct)));
        g.MapPost("/sales/{id:guid}/complete",async(Guid id,HttpContext c,SalesService s,CancellationToken ct)=>Results.Ok(await s.CompleteAsync(id,Key(c),ct)));
        g.MapPost("/sales/{id:guid}/returns",async(Guid id,ReturnRequest req,HttpContext c,ReturnService s,CancellationToken ct)=>Results.Ok(await s.ReturnAsync(id,req,Key(c),ct)));
        g.MapGet("/invoices/{id:guid}",async(Guid id,SalesService s,CancellationToken ct)=>Results.Ok(await s.InvoiceAsync(id,ct)));
        g.MapGet("/invoices/{id:guid}/pdf",async(Guid id,InvoicePdfService s,CancellationToken ct)=>Results.File(await s.RenderAsync(id,ct),"application/pdf","invoice.pdf"));
        g.MapGet("/credit-notes/{id:guid}/pdf",async(Guid id,ReturnService returns,InvoicePdfService pdf,CancellationToken ct)=>Results.File(pdf.Render(await returns.InvoiceAsync(id,ct)),"application/pdf","credit-note.pdf"));
        g.MapGet("/payments",async(Guid branchId,PaymentService s,CancellationToken ct,int page=1,int pageSize=25,Guid? partyId=null,string? search=null)=>Results.Ok(await s.ListAsync(branchId,page,pageSize,partyId,ct,search)));
        g.MapGet("/payments/options",async(Guid branchId,Guid partyId,PaymentService s,CancellationToken ct)=>Results.Ok(await s.OptionsAsync(branchId,partyId,ct)));
        g.MapGet("/payments/{id:guid}/receipt",async(Guid id,PaymentService s,CancellationToken ct)=>Results.Ok(await s.ReceiptAsync(id,ct)));
        g.MapGet("/payments/{id:guid}/receipt/pdf",async(Guid id,PaymentService s,InvoicePdfService pdf,CancellationToken ct)=>Results.File(pdf.RenderReceipt(await s.ReceiptAsync(id,ct)),"application/pdf","payment-receipt.pdf"));
        g.MapPost("/payments",async(PaymentRequest req,HttpContext c,PaymentService s,CancellationToken ct)=>Results.Ok(await s.RecordAsync(req,Key(c),ct)));
        g.MapPost("/payments/{id:guid}/allocations",async(Guid id,AllocationRequest req,HttpContext c,PaymentService s,CancellationToken ct)=>Results.Ok(await s.AllocateAsync(id,req,Key(c),ct)));
        g.MapPost("/payments/{id:guid}/reverse",async(Guid id,ReasonRequest req,HttpContext c,PaymentService s,CancellationToken ct)=>Results.Ok(await s.ReverseAsync(id,req,Key(c),ct)));
        g.MapGet("/stock-transfers",async(Guid branchId,TransferService s,CancellationToken ct)=>Results.Ok(await s.ListAsync(branchId,ct)));
        g.MapPost("/stock-transfers",async(TransferRequest req,HttpContext c,TransferService s,CancellationToken ct)=>Results.Ok(await s.DispatchAsync(req,Key(c),ct)));
        g.MapGet("/stock-transfers/{id:guid}",async(Guid id,TransferService s,CancellationToken ct)=>Results.Ok(await s.DetailAsync(id,ct)));
        g.MapPost("/stock-transfers/{id:guid}/receive",async(Guid id,TransferReceiptRequest req,HttpContext c,TransferService s,CancellationToken ct)=>Results.Ok(await s.ReceiveItemsAsync(id,req,Key(c),ct)));
        g.MapPost("/stock-transfers/{id:guid}/cancel",async(Guid id,ReasonRequest req,HttpContext c,TransferService s,CancellationToken ct)=>Results.Ok(await s.CancelAsync(id,req,Key(c),ct)));
        g.MapGet("/expenses",async(Guid branchId,ExpenseService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null)=>Results.Ok(await s.ListAsync(branchId,page,pageSize,ct,search)));
        g.MapPost("/expenses",async(ExpenseRequest req,HttpContext c,ExpenseService s,CancellationToken ct)=>Results.Ok(await s.CreateAsync(req,Key(c),ct)));
        g.MapPost("/expenses/{id:guid}/reverse",async(Guid id,ReasonRequest req,HttpContext c,ExpenseService s,CancellationToken ct)=>Results.Ok(await s.ReverseAsync(id,req,Key(c),ct)));
        g.MapGet("/dashboard/summary",async(Guid branchId,ReportService s,CancellationToken ct,DateOnly? from=null,DateOnly? to=null)=>Results.Ok(await s.DashboardAsync(branchId,from,to,ct)));
        g.MapPost("/lenden/entries",async(LenDenRequest req,HttpContext c,PaymentService s,CancellationToken ct)=>Results.Ok(await s.RecordLenDenAsync(req,Key(c),ct)));
        g.MapGet("/lenden/accounts",async(Guid branchId,ReportService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null,string? filter=null)=>Results.Ok(await s.AccountsAsync(branchId,page,pageSize,search,ct,filter)));
        g.MapGet("/lenden/outstanding",async(Guid branchId,ReportService s,CancellationToken ct,bool overdue=false,int page=1,int pageSize=25,string? search=null)=>Results.Ok(await s.OutstandingAsync(branchId,overdue,page,pageSize,ct,search)));
        g.MapPost("/lenden/followups",async(FollowUpRequest req,HttpContext c,FollowUpService s,CancellationToken ct)=>Results.Ok(await s.RecordAsync(req,Key(c),ct)));
        g.MapGet("/customers/{id:guid}/followups",async(Guid id,Guid branchId,FollowUpService s,CancellationToken ct,int page=1,int pageSize=25)=>Results.Ok(await s.ListAsync(id,branchId,page,pageSize,ct)));
        g.MapGet("/customers/{id:guid}/statement/pdf",async(Guid id,Guid branchId,FollowUpService s,InvoicePdfService pdf,CancellationToken ct,DateOnly? from=null,DateOnly? to=null)=>Results.File(pdf.RenderStatement(await s.StatementAsync(id,branchId,from,to,ct)),"application/pdf","customer-statement.pdf"));
        g.MapGet("/reports/operations",async(Guid branchId,DateOnly from,DateOnly to,ReportService s,CancellationToken ct)=>Results.Ok(await s.OperationsAsync(branchId,from,to,ct)));
        g.MapGet("/reports/branches",async(DateOnly from,DateOnly to,ReportService s,CancellationToken ct)=>Results.Ok(await s.BranchesAsync(from,to,ct)));
        g.MapGet("/reports/gst",async(Guid branchId,DateOnly from,DateOnly to,ReportService s,CancellationToken ct)=>Results.Ok(await s.GstAsync(branchId,from,to,ct)));
        g.MapGet("/audit-logs",async(ReportService s,CancellationToken ct,int page=1,int pageSize=25)=>Results.Ok(await s.AuditAsync(page,pageSize,ct)));
        g.MapGet("/used-devices",async(Guid branchId,UsedDeviceService s,CancellationToken ct)=>Results.Ok(await s.ListAsync(branchId,ct)));
        g.MapPost("/device-acquisitions",async(AcquisitionRequest req,HttpContext c,UsedDeviceService s,CancellationToken ct)=>Results.Ok(await s.AcquireAsync(req,Key(c),ct)));
        g.MapPost("/purchases/{id:guid}/returns",async(Guid id,PurchaseReturnRequest req,HttpContext c,PurchaseReturnService s,CancellationToken ct)=>Results.Ok(await s.ReturnAsync(id,req,Key(c),ct)));
        g.MapPost("/documents",async(FileUploadRequest req,HttpContext c,DocumentService s,CancellationToken ct)=>Results.Ok(await s.UploadAsync(req,Key(c),ct)));
        g.MapGet("/documents",async(DocumentService s,CancellationToken ct,Guid? purchaseId=null,Guid? expenseId=null)=>Results.Ok(await s.ListAsync(purchaseId,expenseId,ct)));
        g.MapGet("/documents/{id:guid}/download",async(Guid id,DocumentService s,CancellationToken ct)=>{var file=await s.DownloadAsync(id,ct);return Results.File(file.Bytes,file.Mime,file.Name);});
        g.MapGet("/settings",async(SettingsService s,CancellationToken ct)=>Results.Ok(await s.GetAsync(ct)));
        g.MapPut("/settings",async(SettingsRequest req,HttpContext c,SettingsService s,CancellationToken ct)=>Results.Ok(await s.SaveAsync(req,Key(c),ct)));
    }
    private static void MapParties(RouteGroupBuilder g,string path,PartyKind kind)
    {
        g.MapGet("/"+path,async(PartyService s,CancellationToken ct,int page=1,int pageSize=25,string? search=null)=>Results.Ok(await s.ListAsync(kind,page,pageSize,search,ct)));
        g.MapPut("/"+path+"/{id:guid}",async(Guid id,PartyRequest req,HttpContext c,PartyService s,CancellationToken ct)=>Results.Ok(await s.UpdateAsync(id,kind,req,Key(c),ct)));
        g.MapPost("/"+path,async(PartyRequest req,HttpContext c,PartyService s,CancellationToken ct)=>Results.Ok(await s.CreateAsync(kind,req,Key(c),ct)));
        g.MapGet("/"+path+"/{id:guid}",async(Guid id,Guid branchId,PartyService s,CancellationToken ct)=>Results.Ok(await s.DetailAsync(id,kind,branchId,ct)));
        g.MapGet("/"+path+"/{id:guid}/ledger",async(Guid id,Guid branchId,PartyService s,CancellationToken ct,int page=1,int pageSize=25,string? scope=null)=>Results.Ok(await s.LedgerAsync(id,kind,branchId,page,pageSize,ct,scope)));
        g.MapPost("/"+path+"/{id:guid}/notes",async(Guid id,NoteRequest req,HttpContext c,PartyService s,CancellationToken ct)=>Results.Ok(await s.NoteAsync(id,kind,req,Key(c),ct)));
    }
}
public sealed class DecimalStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options){if(reader.TokenType==JsonTokenType.String && decimal.TryParse(reader.GetString(),System.Globalization.NumberStyles.AllowLeadingSign|System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var value))return value;if(reader.TokenType==JsonTokenType.Number && reader.TryGetDecimal(out value))return value;throw new JsonException("Money must be a valid decimal value.");}
    public override void Write(Utf8JsonWriter writer,decimal value,JsonSerializerOptions options)=>writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
public sealed class RetailRequestFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx,EndpointFilterDelegate next)
    {
        foreach(var arg in ctx.Arguments)
        {
            var valid=arg switch{ProductPurchaseRequest x=>x.Product is not null&&(x.DeviceStock is null||x.DeviceStock.All(d=>d is not null&&d.Device is not null)),ProductWithStockRequest x=>x.Product is not null && (x.Devices is null || x.Devices.All(d=>d is not null)) && (x.DeviceStock is null || x.DeviceStock.All(d=>d is not null && d.Device is not null)),DeviceStockBatchRequest x=>x.Devices is not null && x.Devices.All(d=>d is not null&&d.Device is not null),PurchaseRequest x=>x.Items is not null && x.Items.All(i=>i is not null && (i.Devices is null || i.Devices.All(d=>d is not null))),SaleRequest x=>x.Items is not null && x.Payments is not null && x.Items.All(i=>i is not null) && x.Payments.All(i=>i is not null),PaymentRequest x=>x.Allocations is not null && x.Allocations.All(a=>a is not null),ReturnRequest x=>x.Items is not null && x.Items.All(i=>i is not null),PurchaseReturnRequest x=>x.Items is not null && x.Items.All(i=>i is not null),TransferReceiptRequest x=>x.Items is null || x.Items.All(i=>i is not null),TransferRequest x=>x.Items is not null && x.Items.All(i=>i is not null),_=>true};
            if(!valid)return Results.Problem(statusCode:400,title:"Request contains missing or invalid item arrays.");
            if(arg is not null && arg.GetType().Namespace=="Invora.Contracts.Retail")
                foreach(var p in arg.GetType().GetProperties().Where(p=>p.PropertyType==typeof(string))){var v=p.GetValue(arg) as string;if(v?.Length>(arg is ImportRequest && p.Name==nameof(ImportRequest.Csv)?1000000:arg is FileUploadRequest && p.Name==nameof(FileUploadRequest.Base64)?7000000:2000))return Results.Problem(statusCode:400,title:"Text exceeds the supported length.");if(v is null && new System.Reflection.NullabilityInfoContext().Create(p).ReadState==System.Reflection.NullabilityState.NotNull)return Results.Problem(statusCode:400,title:"Required text is missing.");}
        }
        return await next(ctx);
    }
}
