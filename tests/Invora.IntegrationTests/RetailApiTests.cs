using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Identity;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
namespace Invora.IntegrationTests;
public sealed partial class RetailApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres=new PostgreSqlBuilder("postgres:18.6").Build();
    private WebApplicationFactory<Program> factory=null!;private HttpClient client=null!;private Guid branch,supplier,customer,phone,cable;private DateOnly date=new(2026,10,6);
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{b.UseEnvironment("Development");b.UseSetting("ConnectionStrings:Invora",postgres.GetConnectionString());b.UseSetting("Auth:SigningKey","retail-test-signing-key-12345678901234567890");b.UseSetting("Auth:BootstrapKey","retail-test-bootstrap-key-1234567890");b.UseSetting("Auth:BrowserOrigin","http://localhost");});client=factory.CreateClient(new(){HandleCookies=false});
        await using(var scope=factory.Services.CreateAsyncScope())await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Database.MigrateAsync();
        var setup=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/register"){Content=JsonContent.Create(new SetupRequest("Smart Plaza","SAR","Sarangpur","owner","Owner","retail-password-12345"))};setup.Headers.Add("X-Invora-Bootstrap","retail-test-bootstrap-key-1234567890");var response=await client.SendAsync(setup);response.EnsureSuccessStatusCode();var session=(await response.Content.ReadFromJsonAsync<SessionView>())!;branch=session.User.BranchIds[0];client.DefaultRequestHeaders.Authorization=new("Bearer",session.AccessToken);
        var tax=await Id("/tax-rates",new TaxRequest("Zero",0));phone=await Id("/products",new ProductRequest("Phone","Oppo","Smartphone","85171300",tax,"PHONE",null,"8GB","128GB","Green",true,3000,3500));cable=await Id("/products",new ProductRequest("Cable","Generic","Cable","8544",tax,"CABLE","CABLE-BARCODE","","","Black",false,200,250));supplier=await Id("/suppliers",new PartyRequest("Supplier","9000000001"));customer=await Id("/customers",new PartyRequest("Customer","9000000002"));
    }
    public async Task DisposeAsync(){client.Dispose();await factory.DisposeAsync();await postgres.DisposeAsync();}
    private async Task<HttpResponseMessage> Post(string path,object body,string? key=null){var req=new HttpRequestMessage(HttpMethod.Post,"/api/v1"+path){Content=JsonContent.Create(body)};req.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return await client.SendAsync(req);}
    private async Task<Guid> Id(string path,object body){var res=await Post(path,body);if(!res.IsSuccessStatusCode)throw new Exception(await res.Content.ReadAsStringAsync());return (await res.Content.ReadFromJsonAsync<Guid>());}
    private async Task<DocumentView> Document(string path,object body,string? key=null){var res=await Post(path,body,key);if(!res.IsSuccessStatusCode)throw new Exception(await res.Content.ReadAsStringAsync());return (await res.Content.ReadFromJsonAsync<DocumentView>())!;}
    private async Task<DocumentView> Purchase(PurchaseLineRequest[] lines){var draft=await Document("/purchases",new PurchaseRequest(branch,supplier,Guid.NewGuid().ToString(),date,lines));return await Document($"/purchases/{draft.Id}/complete",new{});}
    private async Task<StockUnit> Unit(){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<StockUnit>().AsNoTracking().FirstAsync();}
    private async Task<DocumentView> Sale(Guid? unit,int qty=1,decimal paid=0){var draft=await Document("/sales",new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(unit==null?cable:phone,qty,unit==null?200:3000,unit) ],paid>0?[new(paid,1)]:[]));return await Document($"/sales/{draft.Id}/complete",new{});}
    private async Task ConfigureGst(){var request=new HttpRequestMessage(HttpMethod.Put,"/api/v1/settings"){Content=JsonContent.Create(new SettingsRequest(LegalName:"Smart Plaza",Address:"Synthetic address, Madhya Pradesh",Gstin:"23ABCDE1234F1Z5",GstRegistered:true))};request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());(await client.SendAsync(request)).EnsureSuccessStatusCode();}
    [Fact]public async Task SelectedInvoiceAcceptsPartialAndExcessMoneyWithoutOverallocatingAndRetriesOnce()
    {
        await Purchase([new(cable,1,100)]);var sale=await Sale(null);var partial=await Id("/payments",new PaymentRequest(branch,customer,50,1,date,"PART","Partial payment",[],InvoiceId:sale.Id));
        Assert.Equal(150,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}")).GetProperty("outstanding").Amount());
        var request=new PaymentRequest(branch,customer,200,1,date,"EXCESS","Extra money held",[],InvoiceId:sale.Id);var key=Guid.NewGuid().ToString();var response=await Post("/payments",request,key);response.EnsureSuccessStatusCode();var payment=await response.Content.ReadFromJsonAsync<Guid>();var replay=await Post("/payments",request,key);replay.EnsureSuccessStatusCode();Assert.Equal(payment,await replay.Content.ReadFromJsonAsync<Guid>());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(150,await db.Set<PaymentAllocation>().Where(x=>x.PaymentId==payment).SumAsync(x=>x.Amount));Assert.Equal(200,(await db.Set<Payment>().SingleAsync(x=>x.Id==payment)).Amount);Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{sale.Id}")).GetProperty("outstanding").Amount());
        await Id("/payments",new PaymentRequest(branch,customer,50,1,date,"","Return extra money",[],true));Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}")).GetProperty("tradeBalance").Amount());
        Assert.Equal(HttpStatusCode.NotFound,(await Post("/payments",request with{PartyId=supplier})).StatusCode);
    }
    [Fact]public async Task AutomaticPaymentsSettleOldestDuesWhileExplicitAdvancesAndIndependentMoneyStaySeparate()
    {
        await Purchase([new(cable,2,100)]);var first=await Sale(null);var second=await Sale(null);
        await Id("/payments",new PaymentRequest(branch,customer,300,1,date,"","No invoice choice needed",[],AutoAllocate:true));
        Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{first.Id}")).GetProperty("outstanding").Amount());Assert.Equal(100,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{second.Id}")).GetProperty("outstanding").Amount());
        await Id("/payments",new PaymentRequest(branch,customer,25,1,date,"","Held on account",[]));Assert.Equal(100,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{second.Id}")).GetProperty("outstanding").Amount());
        var account=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");Assert.Equal(75,account.GetProperty("tradeBalance").Amount());Assert.Equal(0,account.GetProperty("independentBalance").Amount());
    }
    [Fact]public async Task SupplierPaymentsCapToBillAndRetainActualExcessAsRecoverableCredit()
    {
        var bill=await Purchase([new(cable,1,100)]);var payment=await Id("/payments",new PaymentRequest(branch,supplier,150,1,date,"","Extra supplier payment",[],InvoiceId:bill.Id));await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(100,await db.Set<PaymentAllocation>().Where(x=>x.PaymentId==payment).SumAsync(x=>x.Amount));Assert.Equal(-50,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{supplier}?branchId={branch}")).GetProperty("balance").Amount());
        await Id("/payments",new PaymentRequest(branch,supplier,50,1,date,"","Supplier returned extra",[],true));Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{supplier}?branchId={branch}")).GetProperty("balance").Amount());
    }
    [Fact]public async Task InventoryCsvUsesVisibleViewFiltersDatesReadableIdentitiesAndCostPermissions()
    {
        await Purchase([new(phone,1,100,Devices:[new("500000000000001","500000000000002","CSV-SERIAL")]),new(cable,3,10)]);
        var csv=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}&view=devices&brand=Oppo&status=InStock");Assert.Contains("Received date",csv);Assert.Contains("Exported at",csv);Assert.Contains("IMEI 1",csv);Assert.Contains("500000000000001",csv);Assert.Contains("CSV-SERIAL",csv);Assert.Contains("Unit cost",csv);Assert.DoesNotContain("productVariantId",csv);
        var quantity=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}&view=quantity&search=CABLE");Assert.Contains("On hand",quantity);Assert.Contains("Last movement",quantity);Assert.Contains("CABLE-BARCODE",quantity);Assert.DoesNotContain("IMEI 1",quantity);
        var empty=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}&view=devices&brand=Apple");Assert.Contains("Received date",empty);Assert.DoesNotContain("500000000000001",empty);
        var movement=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}&view=movements&search=CSV-SERIAL");Assert.Contains("Movement date",movement);Assert.Contains("Oppo Phone",movement);Assert.DoesNotContain("Generic Cable",movement);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/exports/inventory?branchId={branch}&view=unsupported")).StatusCode);
        var staff=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("stock-export","Stock export","staff-test-password-12345",["inventory.view","reports.export"],[branch]));staff.EnsureSuccessStatusCode();var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("stock-export","staff-test-password-12345"));login.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);var restricted=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}");Assert.DoesNotContain("Unit cost",restricted);Assert.Contains("500000000000001",restricted);Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/exports/inventory?branchId={Guid.NewGuid()}")).StatusCode);
    }
    [Fact]public async Task SupplyStateIsNormalizedAndTaxSplitFollowsDeliveryRatherThanStaleCheckbox()
    {
        await ConfigureGst();var person=await Id("/customers",new PartyRequest("Delhi buyer","9000000030",StateCode:" 7 ",Gstin:"07ABCDE1234F1Z5"));var contact=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{person}?branchId={branch}");Assert.Equal("07",contact.GetProperty("party").GetProperty("stateCode").GetString());
        var tax=await Id("/tax-rates",new TaxRequest("GST 18",18));var item=await Id("/products",new ProductRequest("GST fixture cable","Generic","Electronics","8544",tax,"GST-SUPPLY",null,"","","",false,118,118));await Purchase([new(item,3,50)]);
        var request=new SaleRequest(branch,person,date,null,false,[new(item,1,118)],[new(118,1)]);
        var quote=await Post("/sales/quote",request);quote.EnsureSuccessStatusCode();var q=(await quote.Content.ReadFromJsonAsync<JsonElement>());Assert.True(q.GetProperty("interstate").GetBoolean());Assert.Equal("07",q.GetProperty("supplyStateCode").GetString());Assert.Equal(18,decimal.Parse(q.GetProperty("igst").GetString()!));Assert.Equal(0,decimal.Parse(q.GetProperty("cgst").GetString()!));
        var local=request with{Interstate=true,SupplyStateCode="23"};var localQuote=await Post("/sales/quote",local);localQuote.EnsureSuccessStatusCode();q=await localQuote.Content.ReadFromJsonAsync<JsonElement>();Assert.False(q.GetProperty("interstate").GetBoolean());Assert.Equal(9,decimal.Parse(q.GetProperty("cgst").GetString()!));Assert.Equal(9,decimal.Parse(q.GetProperty("sgst").GetString()!));
        var draft=await Document("/sales",local);var sale=await Document($"/sales/{draft.Id}/complete",new{});var invoice=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;Assert.Equal("23",invoice.SupplyStateCode);Assert.Equal("07",invoice.CustomerStateCode);Assert.False(invoice.Interstate);Assert.Equal(118,invoice.Total);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/sales/quote",request with{SupplyStateCode="99"})).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await Post("/customers",new PartyRequest("Incorrect state","9000000031",StateCode:"23",Gstin:"07ABCDE1234F1Z5"))).StatusCode);
        var unknown=await Post("/sales/quote",request with{CustomerId=customer,Interstate=true});unknown.EnsureSuccessStatusCode();Assert.False((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("interstate").GetBoolean());
        var bytes=await client.GetByteArrayAsync($"/api/v1/invoices/{sale.Id}/pdf");var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"));Directory.CreateDirectory(folder);await File.WriteAllBytesAsync(Path.Combine(folder,"invoice-gst-sample.pdf"),bytes);
    }
    [Fact]public async Task SamplePhoneInvoiceKeepsEveryImeiAndTaxAmountWhenPrintingGroupedDevices()
    {
        await ConfigureGst();var tax=await Id("/tax-rates",new TaxRequest("Phone GST",18));var product=new ProductRequest("Mobilephone fixture","Oppo","Mobile","85171300",tax,"PRINT-PHONES",null,"","","",true,3000,3500);var colors=new[]{"Green","Green","Green","White","White","Red","Green","Gold"};var rows=Enumerable.Range(0,8).Select(i=>new DeviceStockLine(new((500000000000010L+i).ToString()),i==5?"12 GB":"8 GB",i is 5 or 6?"256 GB":"128 GB",colors[i],1000+i*100,3000,3500)).ToArray();await Id("/products/with-stock",new ProductWithStockRequest(product,branch,8,0,"Sample print stock",DeviceStock:rows));
        StockUnit[] units;await using(var scope=factory.Services.CreateAsyncScope())units=await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<StockUnit>().OrderBy(x=>x.ReceivedAtUtc).ToArrayAsync();
        var draft=await Document("/sales",new SaleRequest(branch,customer,date,null,false,units.Select(u=>new SaleLineRequest(u.ProductVariantId,1,3000,u.Id)).ToArray(),[new(24000,1)]));var sale=await Document($"/sales/{draft.Id}/complete",new{});var snapshot=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;Assert.Equal(8,snapshot.Lines.Sum(x=>x.Quantity));Assert.Equal(8,snapshot.Lines.SelectMany(x=>x.Identifiers).Count());Assert.Equal(24000,snapshot.Total);Assert.Equal(snapshot.Total,snapshot.Taxable+snapshot.Cgst+snapshot.Sgst);Assert.Equal(0,snapshot.Igst);
        var bytes=await client.GetByteArrayAsync($"/api/v1/invoices/{sale.Id}/pdf");var folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"));Directory.CreateDirectory(folder);await File.WriteAllBytesAsync(Path.Combine(folder,"invoice-phone-sample.pdf"),bytes);
    }
    [Fact]public async Task DatedInvoiceNumbersStayUniqueAcrossBranchesAndFiscalYearsAndPreservePostedSnapshots()
    {
        await Purchase([new(cable,3,100)]);var first=await Sale(null);Assert.Matches(@"^I-06Oct26-000001$",first.Number);Assert.Equal("INV-SP-SAR-06Oct26-000001",first.Reference);Assert.True(first.Number.Length<=16);
        var branchResponse=await Post("/branches",new{code="OTHER",name="Other counter"});branchResponse.EnsureSuccessStatusCode();var otherBranch=(await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();await Id("/inventory/adjustments",new AdjustmentRequest(otherBranch,cable,1,100,"Synthetic other-branch stock"));var request=new SaleRequest(otherBranch,customer,date,date.AddDays(1),false,[new(cable,1,200)],[]);var draft=await Document("/sales",request);var key=Guid.NewGuid().ToString();var other=await Document($"/sales/{draft.Id}/complete",new{},key);Assert.Equal("I-06Oct26-000002",other.Number);Assert.Equal("INV-SP-OTHER-06Oct26-000002",other.Reference);Assert.Equal(other,await Document($"/sales/{draft.Id}/complete",new{},key));
        var nextYear=new DateOnly(2027,4,1);draft=await Document("/sales",request with{BranchId=branch,BusinessDate=nextYear,DueDate=nextYear.AddDays(1)});var third=await Document($"/sales/{draft.Id}/complete",new{});Assert.Equal("I-01Apr27-000001",third.Number);
        var snapshot=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{first.Id}"))!;Assert.Equal(first.Number,snapshot.Number);Assert.Equal(first.Reference,snapshot.Reference);Assert.NotNull(snapshot.IssuedAt);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Sale\" SET \"Reference\"='tampered' WHERE \"Id\"={first.Id}"));
    }
    [Fact]public async Task InvoiceSearchFindsCustomerNameBothFormattedPhonesAndLongReferenceWithinBranch()
    {
        var person=await Id("/customers",new PartyRequest("Ravi Counter Customer","9000000038",AlternatePhone:"919000000039"));await Purchase([new(cable,1,100)]);var draft=await Document("/sales",new SaleRequest(branch,person,date,date.AddDays(1),false,[new(cable,1,200)],[]));var invoice=await Document($"/sales/{draft.Id}/complete",new{});
        foreach(var term in new[]{"customer ravi","+91 90000 00038","+91 90000 00039",invoice.Number,invoice.Reference}){var result=await client.GetFromJsonAsync<Invora.Contracts.Common.PageResponse<DocumentView>>($"/api/v1/sales?branchId={branch}&search="+Uri.EscapeDataString(term));var row=Assert.Single(result!.Items);Assert.Equal(invoice.Id,row.Id);Assert.Equal("Ravi Counter Customer",row.PartyName);Assert.Equal("919000000039",row.AlternatePhone);}
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/sales?branchId={Guid.NewGuid()}&search=Ravi")).StatusCode);
    }
    [Fact]public async Task InventoryStatusBrandAndQuantityBarcodeFiltersReflectExactPostedStock()
    {
        await Purchase([new(phone,2,1000,Devices:[new("500000000000001"),new("500000000000002")]),new(cable,2,100)]);var unit=await Unit();await Sale(unit.Id,1,3000);
        foreach(var status in new[]{"InStock","Sold"}){var result=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&status={status}&brand=Oppo");Assert.Single(result.GetProperty("items").EnumerateArray());Assert.Equal(status,result.GetProperty("items")[0].GetProperty("status").GetString());}
        var mismatch=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&status=Sold&brand=Apple");Assert.Empty(mismatch.GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/inventory?branchId={branch}&status=Unknown")).StatusCode);
        var quantities=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/balances?branchId={branch}&brand=Generic&status=InStock&search=CABLE-BARCODE");Assert.Equal(2,Assert.Single(quantities.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());await Sale(null,2,400);
        quantities=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/balances?branchId={branch}&brand=Generic&status=OutOfStock");Assert.Equal(0,Assert.Single(quantities.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetInt32());
    }
    [Fact]public async Task SupplierCreditsAndSeparateCustomerAccountsShowBothSidesWithoutHidingMoney()
    {
        var purchase=await Purchase([new(cable,2,100)]);await Id("/payments",new PaymentRequest(branch,supplier,300,1,date,"","Supplier paid including advance",[new(null,purchase.Id,200)]));
        var accounts=await client.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/accounts?branchId={branch}&filter=receive");Assert.Equal(-100,decimal.Parse(Assert.Single(accounts.GetProperty("items").EnumerateArray()).GetProperty("balance").GetString()!));Assert.Equal(100,decimal.Parse(accounts.GetProperty("moneyToReceive").GetString()!));Assert.Equal(0,decimal.Parse(accounts.GetProperty("moneyToPay").GetString()!));
        await Sale(null,1,0);await Id("/lenden/entries",new LenDenRequest(branch,customer,"In",500,1,date,"","Independent customer deposit"));var dashboard=await client.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/summary?branchId={branch}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");Assert.Equal(200,decimal.Parse(dashboard.GetProperty("customerOutstanding").GetString()!));Assert.Equal(500,decimal.Parse(dashboard.GetProperty("customerCredit").GetString()!));Assert.Equal(100,decimal.Parse(dashboard.GetProperty("supplierReceivable").GetString()!));Assert.Equal(0,decimal.Parse(dashboard.GetProperty("supplierPayable").GetString()!));
        var detail=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");Assert.Equal(200,decimal.Parse(detail.GetProperty("tradeBalance").GetString()!));Assert.Equal(-500,decimal.Parse(detail.GetProperty("independentBalance").GetString()!));foreach(var scope in new[]{"trade","independent"}){var statement=await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}/ledger?branchId={branch}&scope={scope}");Assert.Single(statement.GetProperty("items").EnumerateArray());Assert.Equal(scope=="trade"?200:-500,decimal.Parse(statement.GetProperty("balance").GetString()!));}
        await Id("/payments",new PaymentRequest(branch,supplier,100,1,date,"","Supplier returned unused advance",[],true));accounts=await client.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/accounts?branchId={branch}&filter=settled");Assert.Single(accounts.GetProperty("items").EnumerateArray());Assert.Equal(0,decimal.Parse(accounts.GetProperty("moneyToReceive").GetString()!));
    }
    [Fact]public async Task PurchasePreparationKeepsPerPhoneVariantsWithoutPostingStockAndIsIdempotent()
    {
        var basis=(await client.GetFromJsonAsync<ProductView>($"/api/v1/products/{phone}"))!;
        var request=new ProductPurchaseRequest(new("Galaxy S24 FE","Samsung","Mobile","85171300",basis.TaxRateId,"PREPARED-FE",null,"8 GB","128 GB","Green",true,3000,3500),2,1000,[new(new("400000000000001"),"8 GB","128 GB","Green",1000,3000,3500),new(new("400000000000002"),"12 GB","256 GB","Blue",1200,3300,3800)]);
        var key=Guid.NewGuid().ToString();var response=await Post("/products/for-purchase",request,key);response.EnsureSuccessStatusCode();var prepared=(await response.Content.ReadFromJsonAsync<PreparedPurchaseLine[]>())!;Assert.Equal(2,prepared.Length);Assert.Single(prepared.Select(x=>x.Product.ProductModelId).Distinct());Assert.Equal(new[]{"Blue","Green"},prepared.Select(x=>x.Product.Color).Order().ToArray());
        var repeat=await Post("/products/for-purchase",request,key);repeat.EnsureSuccessStatusCode();Assert.Equal(await response.Content.ReadAsStringAsync(),await repeat.Content.ReadAsStringAsync());
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Empty(await db.Set<StockUnit>().ToArrayAsync());Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());}
        await Purchase(prepared.Select(x=>new PurchaseLineRequest(x.Product.Id,x.Quantity,x.UnitCost,Devices:x.Devices)).ToArray());
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2200,await db.Set<StockUnit>().SumAsync(x=>x.Cost));Assert.Equal(2,await db.Set<StockUnit>().CountAsync());}
        var invalid=request with{Product=request.Product with{Sku="INVALID-PREPARED"},DeviceStock=[request.DeviceStock![0],request.DeviceStock[0]]};Assert.Equal(HttpStatusCode.Conflict,(await Post("/products/for-purchase",invalid)).StatusCode);
        await using(var scope=factory.Services.CreateAsyncScope())Assert.False(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<ProductVariant>().AnyAsync(x=>x.Sku=="INVALID-PREPARED"));
    }
    [Fact]public async Task SaleReviewRejectsMalformedInputAndReceivesOldPhoneWithoutFabricatingImei()
    {
        await Purchase([new(phone,1,1000,Devices:[new("400000000000010")])]);var unit=await Unit();
        var malformed=await Post("/sales/quote",new{branchId=branch,customerId=customer,businessDate=date,items=new[]{new{productVariantId=phone,quantity=1,unitPrice=3000,stockUnitId=unit.Id}},payments=Array.Empty<object>(),exchange=new{productVariantId=""}});Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);Assert.DoesNotContain("unexpected error",await malformed.Content.ReadAsStringAsync());
        var old=new AcquisitionRequest(branch,customer,null,null,1000,1,"Used","Customer old device checked",null,0,OldDevice:new("Redmi Note 10","Redmi","4 GB","64 GB","Black"),SellerName:"Customer",AadhaarLastFour:"4321");
        var request=new SaleRequest(branch,customer,date,null,false,[new(phone,1,3000,unit.Id)],[new(2000,2)],old);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/sales/quote",request with{Exchange=old with{OldDevice=null}})).StatusCode);
        (await Post("/sales/quote",request)).EnsureSuccessStatusCode();var draft=await Document("/sales",request);await Document($"/sales/{draft.Id}/complete",new{});
        Guid oldId,oldVariant;string tracking;await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var received=await db.Set<StockUnit>().SingleAsync(x=>x.Source=="Exchange");oldId=received.Id;oldVariant=received.ProductVariantId;Assert.Equal(1000,received.Cost);var identifier=await db.Set<UnitIdentifier>().SingleAsync(x=>x.StockUnitId==oldId);Assert.Equal("SHOP",identifier.Kind);tracking=identifier.Value;Assert.Equal(0,await db.Set<UnitIdentifier>().CountAsync(x=>x.StockUnitId==oldId&&x.Kind=="IMEI"));Assert.Equal(2000,await db.Set<Payment>().SumAsync(x=>x.Amount));Assert.Equal(0,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer).SumAsync(x=>x.Debit-x.Credit));Assert.Equal("4321",(await db.Set<DeviceInspection>().SingleAsync()).AadhaarLastFour);}
        var scanned=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/scan?branchId={branch}&value={tracking}");Assert.Equal(oldId,scanned.GetProperty("unit").GetProperty("id").GetGuid());
        (await Post("/inventory/adjustments",new AdjustmentRequest(branch,oldVariant,-1,0,"Damaged old phone",[new(null,TrackingTag:tracking)]))).EnsureSuccessStatusCode();
        await using(var scope=factory.Services.CreateAsyncScope())Assert.Equal(DeviceStatus.WrittenOff,(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<StockUnit>().SingleAsync(x=>x.Id==oldId)).Status);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/device-acquisitions",old with{AadhaarLastFour="123456789012"})).StatusCode);
    }
    [Fact]public async Task SecondContactAndPaymentPromisesAreSearchableAndNeverChangeMoney()
    {
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/customers",new PartyRequest("Duplicate contact", "9000000050",AlternatePhone:"+91 9000000050"))).StatusCode);
        var person=await Id("/customers",new PartyRequest("Promise Customer","9000000050",AlternatePhone:"+91 90000 00051"));var matches=await client.GetFromJsonAsync<JsonElement>("/api/v1/customers?search="+Uri.EscapeDataString("+91 90000 00051"));Assert.Equal(person,matches.GetProperty("items")[0].GetProperty("id").GetGuid());
        await Id("/lenden/entries",new LenDenRequest(branch,person,"Out",500,1,date,"","Loan given"));var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).Date);var followup=new FollowUpRequest(branch,person,"Promise","Will repay tomorrow",today.AddDays(-1),today,500);var key=Guid.NewGuid().ToString();var first=await Id("/lenden/followups",followup);(await Post("/lenden/followups",followup,key)).EnsureSuccessStatusCode();(await Post("/lenden/followups",followup,key)).EnsureSuccessStatusCode();
        var missed=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&filter=missed&search=9000000051");Assert.Single(missed.GetProperty("items").EnumerateArray());Assert.Equal("500.00",missed.GetProperty("items")[0].GetProperty("independentBalance").GetString());
        var pdf=await client.GetAsync($"/api/v1/customers/{person}/statement/pdf?branchId={branch}&from={date:yyyy-MM-dd}&to={today:yyyy-MM-dd}");pdf.EnsureSuccessStatusCode();Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<PaymentFollowUp>().CountAsync());Assert.Single(await db.Set<LedgerEntry>().ToArrayAsync());Assert.Single(await db.Set<Payment>().ToArrayAsync());await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM \"PaymentFollowUp\""));}
        await Id("/lenden/followups",new FollowUpRequest(branch,person,"ClearPromise","New promise will be agreed later"));var clear=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&filter=missed");Assert.Empty(clear.GetProperty("items").EnumerateArray());
        using var anonymous=factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/v1/customers/{person}/statement/pdf?branchId={branch}")).StatusCode);
    }
    [Fact]public async Task PaymentChoicesIncludeOnlySelectedPartyDuesAndTradingRefundCredit()
    {
        await Purchase([new(cable,3,100)]);var sale=await Sale(null,1,0);var another=await Id("/customers",new PartyRequest("Another customer","9000000070"));var draft=await Document("/sales",new SaleRequest(branch,another,date,date.AddDays(5),false,[new(cable,1,200)],[]));await Document($"/sales/{draft.Id}/complete",new{});
        var options=await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments/options?branchId={branch}&partyId={customer}");Assert.Single(options.GetProperty("documents").EnumerateArray());Assert.Equal(sale.Id,options.GetProperty("documents")[0].GetProperty("id").GetGuid());Assert.Equal(200,decimal.Parse(options.GetProperty("documents")[0].GetProperty("outstanding").GetString()!));
        await Id("/payments",new PaymentRequest(branch,customer,300,1,date,"","Advance",[]));await Id("/lenden/entries",new LenDenRequest(branch,customer,"In",1000,1,date,"","Independent deposit"));options=await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments/options?branchId={branch}&partyId={customer}");Assert.Equal(100,decimal.Parse(options.GetProperty("availableCredit").GetString()!));
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/payments",new PaymentRequest(branch,customer,101,1,date,"","Too much refund",[],true))).StatusCode);await Id("/payments",new PaymentRequest(branch,customer,100,1,date,"","Money given back",[],true));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(100,(await db.Set<StockBalance>().SingleAsync()).Quantity*100);Assert.Equal(-1000,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer&&x.Kind.StartsWith("LenDen")).SumAsync(x=>x.Debit-x.Credit));
    }
    [Fact]public async Task LenDenFiltersKeepOpposingTradingAndIndependentBalancesVisible()
    {
        await Purchase([new(cable,1,100)]);await Sale(null,1,0);await Id("/lenden/entries",new LenDenRequest(branch,customer,"In",300,1,date,"","Independent money left with the shop"));
        foreach(var filter in new[]{"owed","credit","trade"}){var report=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&filter={filter}&search=9000000002");Assert.Single(report.GetProperty("items").EnumerateArray());}
        var independent=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&filter=independent&search=9000000002");Assert.Empty(independent.GetProperty("items").EnumerateArray());
    }
    [Fact]public async Task OperationsReportIncludesReturnsOfEarlierSalesInSelectedPeriod()
    {
        await Purchase([new(cable,2,100)]);var sale=await Sale(null,1,200);Guid item;await using(var scope=factory.Services.CreateAsyncScope())item=(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<SaleItem>().SingleAsync()).Id;
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Returned next day",date.AddDays(1),[new(item,1,"Restock")]));var report=await client.GetFromJsonAsync<JsonElement>($"/api/v1/reports/operations?branchId={branch}&from={date.AddDays(1):yyyy-MM-dd}&to={date.AddDays(1):yyyy-MM-dd}");Assert.Equal(-200,decimal.Parse(report.GetProperty("daily")[0].GetProperty("netSales").GetString()!));Assert.Equal(-1,report.GetProperty("topProducts")[0].GetProperty("quantity").GetInt32());Assert.Equal(-200,decimal.Parse(report.GetProperty("topProducts")[0].GetProperty("netSales").GetString()!));Assert.Equal(2,report.GetProperty("quantityStock").GetInt32());
    }
    [Fact]public async Task LookupMatchesCombinedBrandModelMemoryAndFormattedContactQueries()
    {
        var products=await client.GetFromJsonAsync<JsonElement>("/api/v1/products?search="+Uri.EscapeDataString("oppo phone 8/128"));Assert.Single(products.GetProperty("items").EnumerateArray());
        var wrong=await client.GetFromJsonAsync<JsonElement>("/api/v1/products?search="+Uri.EscapeDataString("oppo phone 3/32"));Assert.Empty(wrong.GetProperty("items").EnumerateArray());
        await Purchase([new(phone,1,1000,Devices:[new("300000000000001")])]);
        var stock=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&availableOnly=true&search="+Uri.EscapeDataString("oppo phone 8/128 green"));Assert.Single(stock.GetProperty("items").EnumerateArray());
        var matchingBrand=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&brand=Oppo&category=Mobile&availableOnly=true");Assert.Single(matchingBrand.GetProperty("items").EnumerateArray());
        var differentBrand=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&brand=Samsung&availableOnly=true");Assert.Empty(differentBrand.GetProperty("items").EnumerateArray());
        var differentCategory=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&category=Electronics&availableOnly=true");Assert.Empty(differentCategory.GetProperty("items").EnumerateArray());
        var person=await Id("/customers",new PartyRequest("Rajesh Kumar","9000000088"));var customers=await client.GetFromJsonAsync<JsonElement>("/api/v1/customers?search="+Uri.EscapeDataString("kumar rajesh 9000000088"));Assert.Single(customers.GetProperty("items").EnumerateArray());Assert.Equal(person,customers.GetProperty("items")[0].GetProperty("id").GetGuid());
        var partial=await client.GetFromJsonAsync<JsonElement>("/api/v1/customers?search="+Uri.EscapeDataString("+91 90000 0008"));Assert.Single(partial.GetProperty("items").EnumerateArray());
    }
    [Fact]public async Task CustomPhoneCategoryKeepsImeiTrackingAndCanBeFoundByRootMobileCategory()
    {
        var created=await Post("/categories",new CategoryRequest("Feature phones",true));created.EnsureSuccessStatusCode();var category=await created.Content.ReadFromJsonAsync<CategoryView>();Assert.True(category!.RequiresImei);
        var duplicate=await Post("/categories",new CategoryRequest("feature phones",true));duplicate.EnsureSuccessStatusCode();Assert.Equal("Feature phones",(await duplicate.Content.ReadFromJsonAsync<CategoryView>())!.Name);
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/categories",new CategoryRequest("Feature phones",false))).StatusCode);
        var basis=(await client.GetFromJsonAsync<ProductView>($"/api/v1/products/{phone}"))!;var request=new ProductRequest("Basic phone","Nokia","Feature phones","85171400",basis.TaxRateId,"FEATURE",null,"2 GB","32 GB","Blue",true,1500,1800);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/products",request with{Serialized=false})).StatusCode);var id=await Id("/products",request);var view=(await client.GetFromJsonAsync<ProductView>($"/api/v1/products/{id}"))!;Assert.True(view.RequiresImei);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/inventory/adjustments",new AdjustmentRequest(branch,id,1,500,"Invalid custom phone identity",[new(null,null,"SERIAL-ONLY")]))).StatusCode);
        var filtered=await client.GetFromJsonAsync<JsonElement>("/api/v1/products?category=Mobile&search=Basic");Assert.Single(filtered.GetProperty("items").EnumerateArray());
        (await Post("/inventory/adjustments",new AdjustmentRequest(branch,id,1,500,"Custom category stock",[new("300000000000010")]))).EnsureSuccessStatusCode();
        var stock=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&category=Mobile&brand=Nokia&availableOnly=true");Assert.Single(stock.GetProperty("items").EnumerateArray());
        await using var scope=factory.Services.CreateAsyncScope();Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<ProductCategory>().CountAsync(x=>x.NormalizedName=="FEATURE PHONES"));
    }
    [Fact]public async Task PurchaseReviewRequiresEveryIdentityAndRejectsDuplicatesWithoutPosting()
    {
        async Task<HttpResponseMessage> Review(PurchaseLineRequest[] items)=>await Post("/purchases/quote",new PurchaseRequest(branch,supplier,"REVIEW",date,items));
        Assert.Equal(HttpStatusCode.BadRequest,(await Review([new(phone,2,1000,Devices:[new("300000000000001")])])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Review([new(phone,1,1000,Devices:[new(null,null,"PHONE-SERIAL")])])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Review([new(phone,1,1000,Devices:[new("300000000000001")]),new(phone,1,1000,Devices:[new("300000000000001")])])).StatusCode);
        (await Review([new(phone,2,1000,Devices:[new("300000000000001"),new("300000000000002")])])).EnsureSuccessStatusCode();
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Empty(await db.Set<StockUnit>().ToArrayAsync());Assert.Empty(await db.Set<Purchase>().ToArrayAsync());Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());}
        await Purchase([new(phone,1,1000,Devices:[new("300000000000001")])]);Assert.Equal(HttpStatusCode.Conflict,(await Review([new(phone,1,1000,Devices:[new("300000000000001")])])).StatusCode);
    }
    [Fact]public async Task MixedDeviceBatchKeepsOneModelAndExactSaleReturnCostAndIdentity()
    {
        var tax=await Id("/tax-rates",new TaxRequest("No GST",0));
        var product=new ProductRequest("Galaxy S24 FE","Samsung","Mobile","85171300",tax,"S24FE",null,"8 GB","128 GB","Blue",true,30000,35000);
        var rows=new DeviceStockLine[]{new(new("200000000000001","200000000000002","S24-BLUE"),"8 GB","128 GB","Blue",20000,30000,35000),new(new("200000000000003",null,"S24-BLACK"),"12 GB","256 GB","Black",24000,34000,40000)};
        var id=await Id("/products/with-stock",new ProductWithStockRequest(product,branch,2,0,"Two phones counted",DeviceStock:rows));
        var variants=(await client.GetFromJsonAsync<Invora.Contracts.Common.PageResponse<ProductView>>("/api/v1/products?search=Galaxy"))!.Items;Assert.Equal(2,variants.Count);Assert.Single(variants.Select(v=>v.ProductModelId).Distinct());
        var black=variants.Single(v=>v.Color=="Black");Assert.NotEqual(id,black.Id);
        var available=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&productModelId={black.ProductModelId}&availableOnly=true");Assert.Equal(2,available.GetProperty("items").GetArrayLength());
        var scan=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/scan?branchId={branch}&value=S24-BLACK");var unit=scan.GetProperty("unit");Assert.Equal(black.Id,unit.GetProperty("productVariantId").GetGuid());Assert.Contains("12 GB 256 GB Black",unit.GetProperty("description").GetString());
        var draft=await Document("/sales",new SaleRequest(branch,customer,date,null,false,[new(black.Id,1,34000,unit.GetProperty("id").GetGuid())],[new(34000,1)]));var sale=await Document($"/sales/{draft.Id}/complete",new{});
        Guid saleItem;await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var item=await db.Set<SaleItem>().SingleAsync(x=>x.SaleId==sale.Id);saleItem=item.Id;Assert.Equal(24000,item.Cost);Assert.Contains("Black",item.Description);Assert.Contains("S24-BLACK",item.IdentifiersJson);Assert.Equal(1,await db.Set<StockUnit>().CountAsync(x=>x.Status==DeviceStatus.InStock));}
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Customer returned exact black device",date,[new(saleItem,1,"Restock")]));await Id("/payments",new PaymentRequest(branch,customer,34000,1,date,"","Return refund",[],true));
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<StockUnit>().CountAsync(x=>x.Status==DeviceStatus.InStock));Assert.Equal(44000,await db.Set<StockUnit>().SumAsync(x=>x.Cost));Assert.Equal(0,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer).SumAsync(x=>x.Debit-x.Credit));}
    }
    [Fact]public async Task DeviceBatchDuplicateRollsBackEveryNewVariantAndUnitAndIsIdempotent()
    {
        var rows=new DeviceStockLine[]{new(new("200000000000001"),"2 GB","32 GB","Blue",1000,3000,3500),new(new("200000000000001"),"3 GB","32 GB","Red",1200,3200,3500)};
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/inventory/devices",new DeviceStockBatchRequest(branch,phone,"Bad batch",rows))).StatusCode);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Empty(await db.Set<StockUnit>().ToArrayAsync());Assert.Equal(2,await db.Set<ProductVariant>().CountAsync());}
        rows[1]=rows[1] with{Device=new("200000000000002")};var body=new DeviceStockBatchRequest(branch,phone,"Verified count",rows);var key=Guid.NewGuid().ToString();var first=await Post("/inventory/devices",body,key);first.EnsureSuccessStatusCode();var second=await Post("/inventory/devices",body,key);second.EnsureSuccessStatusCode();Assert.Equal(await first.Content.ReadAsStringAsync(),await second.Content.ReadAsStringAsync());
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<StockUnit>().CountAsync());Assert.Equal(4,await db.Set<ProductVariant>().CountAsync());Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());Assert.Empty(await db.Set<Payment>().ToArrayAsync());}
    }
    [Fact]public async Task MobileRequiresIndividualImeiAndBrandsAndEquivalentTaxRatesDoNotDuplicate()
    {
        var taxes=await client.GetFromJsonAsync<JsonElement>("/api/v1/tax-rates");var zero=taxes.EnumerateArray().First(x=>decimal.Parse(x.GetProperty("rate").GetString()!,System.Globalization.CultureInfo.InvariantCulture)==0).GetProperty("id").GetGuid();
        Assert.Equal(zero,await Id("/tax-rates",new TaxRequest("Duplicate name for same rate",0)));
        var rejected=await Post("/products",new ProductRequest("Untracked mobile","Test","Mobile","85171300",zero,"UNTRACKED",null,"2 GB","32 GB","",false,1000,1000));Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/inventory/adjustments",new AdjustmentRequest(branch,phone,1,500,"Missing IMEI",[new(null,null,"SERIAL-ONLY-PHONE")]))).StatusCode);
        var brands=await client.GetFromJsonAsync<string[]>("/api/v1/brands");Assert.Contains("Apple",brands!);Assert.Contains("Moto",brands!);Assert.Contains("Infinix",brands!);
        foreach(var name in new[]{"Shop Brand","shop brand"}){var response=await Post("/brands",new BrandRequest(name));response.EnsureSuccessStatusCode();}
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1,await db.Set<Brand>().CountAsync(x=>x.NormalizedName=="SHOP BRAND"));Assert.Empty(await db.Set<StockUnit>().ToArrayAsync());
    }
    [Fact]public async Task WritingOffNumericSerialUsesItsIdentityKindAndRejectsMismatchedIdentitySets()
    {
        await Id("/inventory/adjustments",new AdjustmentRequest(branch,phone,2,1000,"Identity kind test",[new("200000000000001",null,"200000000000002"),new("200000000000002")]));
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/inventory/adjustments",new AdjustmentRequest(branch,phone,-1,0,"Wrong identity combination",[new("200000000000001",null,"OTHER-DEVICE")]))).StatusCode);
        await Id("/inventory/adjustments",new AdjustmentRequest(branch,phone,-1,0,"Write off by numeric serial",[new(null,null,"200000000000002")]));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var serial=await db.Set<UnitIdentifier>().SingleAsync(x=>x.Kind=="SERIAL");Assert.Equal(DeviceStatus.WrittenOff,(await db.Set<StockUnit>().SingleAsync(x=>x.Id==serial.StockUnitId)).Status);Assert.Equal(1,await db.Set<StockUnit>().CountAsync(x=>x.Status==DeviceStatus.InStock));
    }
    [Fact]public async Task AddedModelVariantCanBePurchasedAndReturnedToSupplierWithItsOwnCost()
    {
        var basis=(await client.GetFromJsonAsync<ProductView>($"/api/v1/products/{phone}"))!;
        var variant=await Id("/products",new ProductRequest(basis.Name,basis.Brand,basis.Category,basis.Hsn,basis.TaxRateId,"PHONE-RED",null,"3 GB","32 GB","Red",true,2000,2500,basis.WarrantyMonths,basis.ProductModelId));
        var purchase=await Purchase([new(phone,1,1000,Devices:[new("200000000000001")]),new(variant,1,1200,Devices:[new("200000000000002")])]);
        Guid item,unit;await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var row=await db.Set<StockUnit>().SingleAsync(x=>x.ProductVariantId==variant);unit=row.Id;item=row.PurchaseItemId!.Value;Assert.Equal(1200,row.Cost);Assert.Equal(2,await db.Set<ProductModel>().CountAsync());}
        await Id($"/purchases/{purchase.Id}/returns",new PurchaseReturnRequest("Return red phone","RED-RETURN",date,[new(item,1,unit)]));
        await using var final=factory.Services.CreateAsyncScope();var finalDb=final.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1200,(await finalDb.Set<PurchaseReturn>().SingleAsync()).Credit);Assert.Equal(1,await finalDb.Set<StockUnit>().CountAsync(x=>x.Status==DeviceStatus.InStock));
    }
    [Fact]public async Task ProductAndOpeningStockCommitTogetherAndDeviceChooserScopesAvailability()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var tax=await Id("/tax-rates",new TaxRequest("Opening test",0));
        var product=new ProductRequest("Speaker","Test","Electronics","8518",tax,"NEW-STOCK",null,"","","Black",true,500,600);
        var bad=await Post("/products/with-stock",new ProductWithStockRequest(product,branch,1,250,"Counted existing stock",[new("100000000000001")]));Assert.Equal(HttpStatusCode.Conflict,bad.StatusCode);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.False(await db.Set<ProductVariant>().AnyAsync(x=>x.Sku=="NEW-STOCK"));Assert.Equal(2,await db.Set<ProductModel>().CountAsync());}
        var body=new ProductWithStockRequest(product,branch,1,250,"Counted existing stock",[new(null,null,"SPEAKER-0001")]);var key=Guid.NewGuid().ToString();var id=await IdWithKey(body,key);Assert.Equal(id,await IdWithKey(body,key));
        var available=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}&availableOnly=true&productVariantId={id}");Assert.Single(available.GetProperty("items").EnumerateArray());Assert.Contains("SERIAL: SPEAKER-0001",available.GetProperty("items")[0].GetProperty("identifiers").EnumerateArray().Select(x=>x.GetString()));
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<StockUnit>().CountAsync());Assert.Empty(await db.Set<Payment>().ToArrayAsync());Assert.Equal(1,await db.Set<LedgerEntry>().CountAsync());}
        async Task<Guid> IdWithKey(object request,string requestKey){var response=await Post("/products/with-stock",request,requestKey);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<Guid>();}
    }
    [Fact]public async Task IndependentLenDenDoesNotSettleInvoicesOrAlterSalesRevenueAndCanBeReversed()
    {
        var given=await Id("/lenden/entries",new LenDenRequest(branch,customer,"Out",1000,1,date,"","Personal money lent"));
        var received=await Id("/lenden/entries",new LenDenRequest(branch,customer,"In",400,2,date,"UPI","Partial repayment"));
        await Purchase([new(cable,1,50)]);var sale=await Sale(null,paid:200);
        Assert.Equal(HttpStatusCode.Conflict,(await Post($"/payments/{received}/allocations",new AllocationRequest(sale.Id,null,1))).StatusCode);
        var accounts=await client.GetFromJsonAsync<JsonElement>($"/api/v1/lenden/accounts?branchId={branch}&search=9000000002");var account=accounts.GetProperty("items")[0];Assert.Equal(0,decimal.Parse(account.GetProperty("tradeBalance").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(600,decimal.Parse(account.GetProperty("independentBalance").GetString()!,System.Globalization.CultureInfo.InvariantCulture));
        var summary=await client.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/summary?branchId={branch}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");Assert.Equal(200,decimal.Parse(summary.GetProperty("netSales").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(600,decimal.Parse(summary.GetProperty("paymentsReceived").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(1000,decimal.Parse(summary.GetProperty("paymentsPaid").GetString()!,System.Globalization.CultureInfo.InvariantCulture));
        await Id($"/payments/{received}/reverse",new ReasonRequest("Repayment was entered in error"));await Id($"/payments/{given}/reverse",new ReasonRequest("Loan was entered in error"));
        await Id("/lenden/entries",new LenDenRequest(branch,customer,"In",100,1,date,"","Independent deposit"));
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/payments",new PaymentRequest(branch,customer,1,1,date,"","Cannot refund independent deposit as invoice credit",[],true))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(-100,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer && x.Kind.StartsWith("LenDen")).SumAsync(x=>x.Debit-x.Credit));Assert.Equal(1,await db.Set<PaymentAllocation>().CountAsync());
    }
    [Fact]public async Task PhoneLookupAcceptsCountryCodeAndLenDenRequiresExplicitTrustGrant()
    {
        var results=await client.GetFromJsonAsync<JsonElement>("/api/v1/customers?search="+Uri.EscapeDataString("+91 90000 00002"));Assert.Single(results.GetProperty("items").EnumerateArray());
        (await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("cashier","Cashier","cashier-password-1234",["inventory.view","sales.create","customers.view","customers.manage","payments.create","customers.credit.view"],[branch]))).EnsureSuccessStatusCode();
        using var limited=factory.CreateClient(new(){HandleCookies=false});var login=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("cashier","cashier-password-1234"));login.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        var context=await limited.GetAsync("/api/v1/billing-context");context.EnsureSuccessStatusCode();Assert.DoesNotContain("accountNumber",await context.Content.ReadAsStringAsync());
        var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/lenden/entries"){Content=JsonContent.Create(new LenDenRequest(branch,customer,"Out",100,1,date,"","Not permitted"))};request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.Forbidden,(await limited.SendAsync(request)).StatusCode);
    }
    [Fact]public async Task LoweringUnitPriceRequiresDiscountPermissionAndRollsBack()
    {
        await Purchase([new(cable,1,50)]);
        var draft=await Document("/sales",new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(cable,1,100)],[]));
        (await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("cashier","Cashier","cashier-password-1234",["sales.create","customers.credit.view"],[branch]))).EnsureSuccessStatusCode();
        using var limited=factory.CreateClient(new(){HandleCookies=false});var login=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("cashier","cashier-password-1234"));login.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/sales/{draft.Id}/complete"){Content=JsonContent.Create(new{})};request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.Forbidden,(await limited.SendAsync(request)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1,await db.Set<StockBalance>().Where(x=>x.ProductVariantId==cable).SumAsync(x=>x.Quantity));Assert.False(await db.Set<LedgerEntry>().AnyAsync(x=>x.Kind=="Sale"));
    }
    [Fact]public async Task CrossSlotIdentifiersAndFailedPurchaseRollback()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001","100000000000002")])]);var duplicate=await Document("/purchases",new PurchaseRequest(branch,supplier,"BAD",date,[new(phone,1,1000,Devices:[new("100000000000002")])]));Assert.Equal(HttpStatusCode.Conflict,(await Post($"/purchases/{duplicate.Id}/complete",new{})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1,await db.Set<StockUnit>().CountAsync());Assert.Equal(2,await db.Set<UnitIdentifier>().CountAsync());Assert.Equal(1,await db.Set<LedgerEntry>().CountAsync());
    }
    [Fact]public async Task ConcurrentSaleCanSellDeviceOnlyOnce()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var unit=await Unit();var body=new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(phone,1,3000,unit.Id)],[]);var first=await Document("/sales",body);var second=await Document("/sales",body);var results=await Task.WhenAll(Post($"/sales/{first.Id}/complete",new{}),Post($"/sales/{second.Id}/complete",new{}));Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1,await db.Set<LedgerEntry>().CountAsync(x=>x.Kind=="Sale"));Assert.Equal(DeviceStatus.Sold,(await db.Set<StockUnit>().SingleAsync()).Status);
    }
    [Fact]public async Task PaymentsReturnsRefundAndInvoiceReconcile()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var sale=await Sale((await Unit()).Id,paid:1000);
        var receipt=new PaymentRequest(branch,customer,2000,2,date,"UPI-TEST","Balance received",[new(sale.Id,null,2000)]);var key=Guid.NewGuid().ToString();var paid=await Post("/payments",receipt,key);paid.EnsureSuccessStatusCode();var retry=await Post("/payments",receipt,key);retry.EnsureSuccessStatusCode();Assert.Equal(await paid.Content.ReadAsStringAsync(),await retry.Content.ReadAsStringAsync());
        var pdf=await client.GetAsync($"/api/v1/invoices/{sale.Id}/pdf");if(!pdf.IsSuccessStatusCode)throw new Exception(await pdf.Content.ReadAsStringAsync());Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        Guid line;await using(var scope=factory.Services.CreateAsyncScope())line=(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<SaleItem>().SingleAsync()).Id;
        var returned=await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Customer return",date,[new(line,1,"Restock")]));Assert.Equal(3000,returned.Total);
        var refund=await Id("/payments",new PaymentRequest(branch,customer,3000,1,date,"","Refund",[],true));Assert.NotEqual(Guid.Empty,refund);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(0,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer).SumAsync(x=>x.Debit-x.Credit));Assert.Equal(DeviceStatus.InStock,(await db.Set<StockUnit>().SingleAsync()).Status);Assert.Equal(3000,await db.Set<AllocationReversal>().SumAsync(x=>x.Amount));}
        Assert.Equal(HttpStatusCode.Conflict,(await Post($"/sales/{sale.Id}/returns",new ReturnRequest("Duplicate",date,[new(line,1,"Restock")]))).StatusCode);
        var dashboard=await client.GetAsync($"/api/v1/dashboard/summary?branchId={branch}&from=2026-10-06&to=2026-10-06");dashboard.EnsureSuccessStatusCode();var dues=await client.GetAsync($"/api/v1/lenden/outstanding?branchId={branch}");dues.EnsureSuccessStatusCode();
    }
    [Fact]public async Task QuantityCostAndFifoPartialReturnsReconcile()
    {
        await Purchase([new(cable,3,0.01m,Discount:0.01m)]);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(0.02m,await db.Set<StockCostLayer>().SumAsync(x=>x.UnitCost*x.RemainingQuantity));}
        var sale=await Sale(null,2,400);Guid item;await using(var scope=factory.Services.CreateAsyncScope())item=(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<SaleItem>().SingleAsync()).Id;
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Partial return",date,[new(item,1,"Restock")]));await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,(await db.Set<StockBalance>().SingleAsync()).Quantity);Assert.Equal(2,await db.Set<StockCostLayer>().SumAsync(x=>x.RemainingQuantity));}
    }
    [Fact]public async Task TransferRemovesAvailabilityUntilReceipt()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var response=await client.PostAsJsonAsync("/api/v1/branches",new BranchRequest("IND","Indore"));response.EnsureSuccessStatusCode();var dest=(await response.Content.ReadFromJsonAsync<BranchView>())!;var unit=await Unit();var transfer=await Document("/stock-transfers",new TransferRequest(branch,dest.Id,"Dispatch",[new(phone,1,unit.Id)]));
        var sale=await Document("/sales",new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(phone,1,3000,unit.Id)],[]));Assert.Equal(HttpStatusCode.Conflict,(await Post($"/sales/{sale.Id}/complete",new{})).StatusCode);await Document($"/stock-transfers/{transfer.Id}/receive",new{});await using var scope=factory.Services.CreateAsyncScope();var stockUnit=await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<StockUnit>().SingleAsync();Assert.Equal(dest.Id,stockUnit.BranchId);Assert.Equal(DeviceStatus.InStock,stockUnit.Status);
    }
    [Fact]public async Task RefundedAdvanceCannotSettleAnotherInvoice()
    {
        var advance=await Id("/payments",new PaymentRequest(branch,customer,200,1,date,"","Advance",[]));
        await Id("/payments",new PaymentRequest(branch,customer,200,1,date,"","Advance refunded",[],true));
        await Purchase([new(cable,1,50)]);var sale=await Sale(null);
        Assert.Equal(HttpStatusCode.Conflict,(await Post($"/payments/{advance}/allocations",new AllocationRequest(sale.Id,null,200))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(200,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer).SumAsync(x=>x.Debit-x.Credit));Assert.Empty(await db.Set<PaymentAllocation>().ToArrayAsync());
    }
    [Fact]public async Task AtomicExchangeCreatesNonCashCreditAndCancellationReleasesIt()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var unit=await Unit();
        var request=new SaleRequest(branch,customer,date,date.AddDays(7),false,[new(phone,1,3000,unit.Id)],[new(2000,2)],new(branch,customer,phone,new("100000000000003","100000000000004"),1000,1,"Used","Checked",85,30));
        var draft=await Document("/sales",request);var sale=await Document($"/sales/{draft.Id}/complete",new{});
        Guid line;await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2000,await db.Set<Payment>().SumAsync(x=>x.Amount));Assert.Equal(1000,await db.Set<SaleNonCashSettlement>().SumAsync(x=>x.Amount));Assert.Equal(0,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer).SumAsync(x=>x.Debit-x.Credit));line=(await db.Set<SaleItem>().SingleAsync()).Id;}
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Sale cancelled",date,[new(line,1,"Restock")],true));
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1000,await db.Set<NonCashSettlementReversal>().SumAsync(x=>x.Amount));Assert.Equal(-3000,await db.Set<LedgerEntry>().Where(x=>x.PartyId==customer).SumAsync(x=>x.Debit-x.Credit));Assert.Equal(2,await db.Set<StockUnit>().CountAsync(x=>x.Status==DeviceStatus.InStock));}
    }
    [Fact]public async Task SupplierReturnReleasesAllocationAndRefundReconcilesPayable()
    {
        var purchase=await Purchase([new(cable,3,100)]);await Id("/payments",new PaymentRequest(branch,supplier,300,4,date,"BANK","Supplier paid",[new(null,purchase.Id,300)]));
        Guid item;await using(var scope=factory.Services.CreateAsyncScope())item=(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<PurchaseItem>().SingleAsync()).Id;
        await Id($"/purchases/{purchase.Id}/returns",new PurchaseReturnRequest("Wrong accessories","VENDOR-CN-1",date,[new(item,1)]));await Id("/payments",new PaymentRequest(branch,supplier,100,4,date,"REFUND","Supplier refund",[],true));
        await using var final=factory.Services.CreateAsyncScope();var db=final.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(0,await db.Set<LedgerEntry>().Where(x=>x.PartyId==supplier).SumAsync(x=>x.Debit-x.Credit));Assert.Equal(2,(await db.Set<StockBalance>().SingleAsync()).Quantity);Assert.Equal(100,await db.Set<AllocationReversal>().SumAsync(x=>x.Amount));
    }
    [Fact]public async Task FailedExchangeRollsBackSaleStockAndPayment()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var unit=await Unit();
        var draft=await Document("/sales",new SaleRequest(branch,customer,date,null,false,[new(phone,1,3000,unit.Id)],[new(2000,1)],new(branch,customer,phone,new("100000000000001"),1000,1,"Used","Invalid reacquisition of sold item",80,30)));
        Assert.False((await Post($"/sales/{draft.Id}/complete",new{})).IsSuccessStatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(DeviceStatus.InStock,(await db.Set<StockUnit>().SingleAsync()).Status);Assert.Empty(await db.Set<Payment>().ToArrayAsync());Assert.Equal("Draft",(await db.Set<Sale>().SingleAsync()).Status);Assert.Empty(await db.Set<SaleItem>().ToArrayAsync());
    }

    [Fact]public async Task ReviewedImportCommitsAtomicallyAndRejectsInvalidBatch()
    {
        var valid=await Post("/imports/validate",new ImportRequest("customers","name,phone,address\r\nCSV Customer,9000000010,\"Address, quoted\"\r\n"));valid.EnsureSuccessStatusCode();var job=await valid.Content.ReadFromJsonAsync<JsonElement>();var id=job.GetProperty("id").GetGuid();Assert.Equal("Validated",job.GetProperty("status").GetString());(await Post($"/imports/{id}/commit",new{})).EnsureSuccessStatusCode();
        var invalid=await Post("/imports/validate",new ImportRequest("customers","name,phone\nBad Customer,123\n"));invalid.EnsureSuccessStatusCode();var bad=await invalid.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("Invalid",bad.GetProperty("status").GetString());Assert.Equal(HttpStatusCode.BadRequest,(await Post($"/imports/{bad.GetProperty("id").GetGuid()}/commit",new{})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();Assert.Equal("Address, quoted",(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<Party>().SingleAsync(x=>x.Name=="CSV Customer")).Address);
    }
    [Fact]public async Task DatabaseRejectsDirectMutationOfPostedHistory()
    {
        await Purchase([new(cable,2,100)]);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE \"LedgerEntry\" SET \"Credit\" = 1"));Assert.Equal(200,await db.Set<LedgerEntry>().SumAsync(x=>x.Credit));
    }
    [Fact]public async Task PartialTransferReceiptAndRemainderCancellationPreserveStock()
    {
        await Purchase([new(cable,5,100)]);var created=await client.PostAsJsonAsync("/api/v1/branches",new BranchRequest("IND","Indore"));created.EnsureSuccessStatusCode();var destination=(await created.Content.ReadFromJsonAsync<BranchView>())!;var transfer=await Document("/stock-transfers",new TransferRequest(branch,destination.Id,"Partial dispatch",[new(cable,4)]));Guid item;await using(var scope=factory.Services.CreateAsyncScope())item=(await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Set<StockTransferItem>().SingleAsync()).Id;
        var partial=await Document($"/stock-transfers/{transfer.Id}/receive",new TransferReceiptRequest([new(item,2)]));Assert.Equal("PartiallyReceived",partial.Status);await Document($"/stock-transfers/{transfer.Id}/cancel",new ReasonRequest("Remaining two physically returned"));await using var final=factory.Services.CreateAsyncScope();var db=final.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(3,(await db.Set<StockBalance>().SingleAsync(x=>x.BranchId==branch)).Quantity);Assert.Equal(2,(await db.Set<StockBalance>().SingleAsync(x=>x.BranchId==destination.Id)).Quantity);Assert.Equal(500,await db.Set<StockCostLayer>().SumAsync(x=>x.UnitCost*x.RemainingQuantity));
    }

    [Fact]public async Task ProductSearchAndBranchReportsExecuteRealDatabaseQueries()
    {
        var products=await client.GetAsync("/api/v1/products?search=oppo&pageSize=100");products.EnsureSuccessStatusCode();var body=await products.Content.ReadFromJsonAsync<JsonElement>();Assert.Single(body.GetProperty("items").EnumerateArray());(await client.GetAsync($"/api/v1/reports/branches?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}")).EnsureSuccessStatusCode();(await client.GetAsync($"/api/v1/reports/gst?branchId={branch}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}")).EnsureSuccessStatusCode();
    }
    [Fact]public async Task AttachmentsAreValidatedAndPrivateAndReceiptIsPrintable()
    {
        var purchase=await Purchase([new(cable,1,100)]);var content=System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nIsolated test document");var attached=await Post("/documents",new FileUploadRequest(purchase.Id,null,"supplier.pdf",Convert.ToBase64String(content)));attached.EnsureSuccessStatusCode();var file=await attached.Content.ReadFromJsonAsync<JsonElement>();var download=await client.GetAsync($"/api/v1/documents/{file.GetProperty("id").GetGuid()}/download");download.EnsureSuccessStatusCode();Assert.Equal(content,await download.Content.ReadAsByteArrayAsync());var invalid=await Post("/documents",new FileUploadRequest(purchase.Id,null,"unsafe.svg",Convert.ToBase64String("<svg/>"u8.ToArray())));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var payment=await Id("/payments",new PaymentRequest(branch,supplier,100,1,date,"REF","Supplier payment",[new(null,purchase.Id,100)]));var receipt=await client.GetAsync($"/api/v1/payments/{payment}/receipt/pdf");receipt.EnsureSuccessStatusCode();Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString((await receipt.Content.ReadAsByteArrayAsync())[..4]));
        using var anonymous=factory.CreateClient(new(){HandleCookies=false});Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/v1/documents/{file.GetProperty("id").GetGuid()}/download")).StatusCode);
    }

    [Fact]public async Task RetailReadScopesAndCostPermissionsAreEnforced()
    {
        await Purchase([new(phone,1,1000,Devices:[new("100000000000001")])]);var destinationResponse=await client.PostAsJsonAsync("/api/v1/branches",new BranchRequest("IND","Indore"));destinationResponse.EnsureSuccessStatusCode();var destination=(await destinationResponse.Content.ReadFromJsonAsync<BranchView>())!;
        var staffResponse=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("viewer","Viewer","viewer-password-1234",["inventory.view","sales.view","reports.view","audit.view"],[branch]));staffResponse.EnsureSuccessStatusCode();using var limited=factory.CreateClient(new(){HandleCookies=false});var login=await limited.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("viewer","viewer-password-1234"));login.EnsureSuccessStatusCode();limited.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        var inventory=await limited.GetFromJsonAsync<JsonElement>($"/api/v1/inventory?branchId={branch}");Assert.Equal(JsonValueKind.Null,inventory.GetProperty("items")[0].GetProperty("cost").ValueKind);Assert.Equal(HttpStatusCode.NotFound,(await limited.GetAsync($"/api/v1/inventory?branchId={destination.Id}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync($"/api/v1/exports/inventory?branchId={branch}")).StatusCode);
        var audit=await limited.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs");Assert.All(audit.GetProperty("items").EnumerateArray(),row=>{Assert.Equal("{}",row.GetProperty("detailsJson").GetString());Assert.All(row.GetProperty("branchIds").EnumerateArray(),b=>Assert.Equal(branch,b.GetGuid()));});
        var export=await client.GetAsync($"/api/v1/exports/catalog?branchId={branch}");export.EnsureSuccessStatusCode();Assert.Contains("PHONE",await export.Content.ReadAsStringAsync());
    }

    [Fact]public async Task GstInvoiceSnapshotAndMultiPagePdfPreserveTotals()
    {
        var settings=new SettingsRequest(LegalName:"Synthetic Invoice Fixture Store",Address:"Test-only address, Sarangpur",Phone:"9000000000",Gstin:"23ABCDE1234F1Z5",GstRegistered:true,BankName:"Synthetic Bank",AccountHolder:"Synthetic Store",AccountNumber:"0000000000",Ifsc:"TEST0000001",Jurisdiction:"Test fixture jurisdiction");var settingsRequest=new HttpRequestMessage(HttpMethod.Put,"/api/v1/settings"){Content=JsonContent.Create(settings)};settingsRequest.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());(await client.SendAsync(settingsRequest)).EnsureSuccessStatusCode();
        var gst=await Id("/tax-rates",new TaxRequest("GST 18",18));var product=await Id("/products",new ProductRequest("Long product description for repeated invoice rows with commercial tax details","Fixture Brand","Accessory","8544",gst,"PDF-FIXTURE",null,"","","Green",false,118,118));await Purchase([new(product,40,50)]);var draft=await Document("/sales",new SaleRequest(branch,customer,date,null,false,Enumerable.Range(0,40).Select(_=>new SaleLineRequest(product,1,118)).ToArray(),[new(4720,1)]));var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var invoice=await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}");Assert.Equal(4720,invoice!.Total);Assert.Equal(4000,invoice.Taxable);Assert.Equal(360,invoice.Cgst);Assert.Equal(360,invoice.Sgst);Assert.Equal(40,invoice.Lines.Length);
        var pdf=await client.GetAsync($"/api/v1/invoices/{sale.Id}/pdf");pdf.EnsureSuccessStatusCode();var bytes=await pdf.Content.ReadAsByteArrayAsync();Assert.True(System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes),@"/Type\s*/Page\b").Count>=2);var directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts"));Directory.CreateDirectory(directory);await File.WriteAllBytesAsync(Path.Combine(directory,"invoice-multipage-fixture.pdf"),bytes);
    }

    [Fact]public async Task SupplierInvoiceUniquenessUsesFinancialYearRatherThanCalendarYear()
    {
        var first=await Document("/purchases",new PurchaseRequest(branch,supplier,"FY-CHECK",new(2026,10,6),[new(cable,1,100)]));await Document($"/purchases/{first.Id}/complete",new{});var duplicate=await Document("/purchases",new PurchaseRequest(branch,supplier,"FY-CHECK",new(2027,3,1),[new(cable,1,100)]));Assert.Equal(HttpStatusCode.Conflict,(await Post($"/purchases/{duplicate.Id}/complete",new{})).StatusCode);var next=await Document("/purchases",new PurchaseRequest(branch,supplier,"FY-CHECK",new(2027,4,1),[new(cable,1,100)]));await Document($"/purchases/{next.Id}/complete",new{});
        var changed=await client.PutAsJsonAsync("/api/v1/business",new ProfileRequest("Smart Plaza","Asia/Kolkata",1));Assert.Equal(HttpStatusCode.BadRequest,changed.StatusCode);
    }

    [Fact]public async Task ReviewedOpeningStockImportCreatesInventoryWithoutFabricatingMoney()
    {
        var preview=await Post("/imports/validate",new ImportRequest("opening-stock","sku,quantity,unitCost,imei1,imei2,serial,reason\nCABLE,10,100,,,,Opening count\n",branch));preview.EnsureSuccessStatusCode();var job=await preview.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("Validated",job.GetProperty("status").GetString());(await Post($"/imports/{job.GetProperty("id").GetGuid()}/commit",new{})).EnsureSuccessStatusCode();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(10,(await db.Set<StockBalance>().SingleAsync()).Quantity);Assert.Equal(1000,await db.Set<StockCostLayer>().SumAsync(x=>x.UnitCost*x.RemainingQuantity));Assert.Empty(await db.Set<Payment>().ToArrayAsync());Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());
    }

}
