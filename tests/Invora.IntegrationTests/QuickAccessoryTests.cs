using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Identity;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    private async Task<AccessoryBillingPreset[]> Preset(AccessoryPresetRequest body)
    {
        var request=new HttpRequestMessage(HttpMethod.Put,"/api/v1/accessory-billing-defaults"){Content=JsonContent.Create(body)};request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());var result=await client.SendAsync(request);result.EnsureSuccessStatusCode();return (await result.Content.ReadFromJsonAsync<AccessoryBillingPreset[]>())!;
    }
    private async Task<QuickAccessoryResult> QuickAccessory(QuickAccessoryRequest input,string? key=null)
    {
        var result=await Post("/inventory/quick-accessories",input,key);if(!result.IsSuccessStatusCode)throw new Exception(await result.Content.ReadAsStringAsync());return (await result.Content.ReadFromJsonAsync<QuickAccessoryResult>())!;
    }
    private async Task<QuickAccessoryProduct> AccessoryProduct(string name="Clear case")
    {
        var tax=await Id("/tax-rates",new TaxRequest("Accessory GST 18",18));return new(name,118,"Phone cases",Hsn:"39269099",TaxRateId:tax);
    }

    [Fact]
    public async Task QuickAccessoryRecordsStockAndBillsWithExactTaxPaymentProfitAndReturnHistory()
    {
        await ConfigureGst();var product=await AccessoryProduct();await Preset(new(product.Category,product.Hsn!,product.TaxRateId));
        var input=new QuickAccessoryRequest(branch,5,60,Product:new(product.Name,118,product.Category));
        var created=await QuickAccessory(input);Assert.Equal(5,created.OnHand);Assert.Equal(5,created.QuantityAdded);Assert.False(created.Product.Serialized);Assert.StartsWith("ITEM-",created.Product.Sku);Assert.Equal(118,created.Product.Mrp);Assert.Equal("Generic",created.Product.Brand);Assert.Equal(0,created.Product.WarrantyMonths);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());Assert.Empty(await db.Set<Purchase>().ToArrayAsync());Assert.Empty(await db.Set<Payment>().ToArrayAsync());Assert.Equal("CounterStock",(await db.Set<InventoryMovement>().SingleAsync()).Kind);Assert.Equal(300,await db.Set<StockCostLayer>().SumAsync(x=>x.UnitCost*x.RemainingQuantity));
        var request=new SaleRequest(branch,customer,date,null,false,[new(created.Product.Id,2,118)],[new(100,1),new(136,2)]);var draft=await Document("/sales",request);var sale=await Document($"/sales/{draft.Id}/complete",new{});
        var summary=await ValuationSummary();Assert.Equal(80,summary.GetProperty("grossProfit").Amount());Assert.Equal(180,summary.GetProperty("stockPurchaseValue").Amount());Assert.Equal(236,summary.GetProperty("paymentsReceived").Amount());Assert.Equal(0,summary.GetProperty("customerOutstanding").Amount());
        var snapshot=(await client.GetFromJsonAsync<InvoiceSnapshot>($"/api/v1/invoices/{sale.Id}"))!;Assert.Equal(200,snapshot.Lines[0].Taxable);Assert.Equal(18,snapshot.Lines[0].Cgst);Assert.Equal(18,snapshot.Lines[0].Sgst);Assert.Equal(product.Hsn,snapshot.Lines[0].Hsn);
        var line=await db.Set<SaleItem>().SingleAsync();await Document($"/sales/{sale.Id}/returns",new ReturnRequest("One returned",date,[new(line.Id,1,"Restock")]));
        Assert.Equal(4,(await db.Set<StockBalance>().AsNoTracking().SingleAsync()).Quantity);Assert.Equal(40,(await ValuationSummary()).GetProperty("grossProfit").Amount());
        var labels=await client.GetFromJsonAsync<JsonElement>(LabelPath("product",created.Product.Id));var code=labels[0].GetProperty("code").GetString()!;Assert.Equal(created.Product.Id,(await ScanLabel(code)).GetProperty("productVariantId").GetGuid());
        var availability=await client.GetFromJsonAsync<Dictionary<Guid,int>>($"/api/v1/inventory/quantity-availability?branchId={branch}&ids={created.Product.Id}");Assert.Equal(4,availability![created.Product.Id]);
    }

    [Fact]
    public async Task QuickAccessoryRetryIsExactlyOnceAndDuplicateCatalogEntriesAreRejected()
    {
        var product=await AccessoryProduct();var input=new QuickAccessoryRequest(branch,3,50,Product:product,RememberBilling:true);var key=Guid.NewGuid().ToString();
        var first=await QuickAccessory(input,key);Assert.Equal(first,await QuickAccessory(input,key));
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/inventory/quick-accessories",input with{Quantity=4},key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/inventory/quick-accessories",input with{Product=product with{Name=" clear CASE "}})).StatusCode);
        var second=await QuickAccessory(new(branch,2,70,ProductVariantId:first.Product.Id));Assert.Equal(first.Product.Id,second.Product.Id);Assert.Equal(5,second.OnHand);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(3,await db.Set<ProductVariant>().CountAsync());Assert.Equal(2,await db.Set<InventoryMovement>().CountAsync());Assert.Equal(290,await db.Set<StockCostLayer>().SumAsync(x=>x.UnitCost*x.RemainingQuantity));
        var sale=await Document("/sales",new SaleRequest(branch,customer,date,date.AddDays(1),false,[new(first.Product.Id,6,118)],[]));Assert.Equal(HttpStatusCode.Conflict,(await Post($"/sales/{sale.Id}/complete",new{})).StatusCode);Assert.Equal(5,(await db.Set<StockBalance>().AsNoTracking().SingleAsync()).Quantity);Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());
        // Different request keys racing to create the same catalog identity cannot duplicate it.
        var raced=input with{Product=product with{Name="Concurrent case"},RememberBilling=false};var results=await Task.WhenAll(Post("/inventory/quick-accessories",raced),Post("/inventory/quick-accessories",raced));Assert.Single(results,x=>x.IsSuccessStatusCode);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task QuickAccessoryRejectsMissingCostsBadBillingAndSerializedItemsWithoutPartialWrites()
    {
        var product=await AccessoryProduct();var valid=new QuickAccessoryRequest(branch,1,50,Product:product);
        foreach(var input in new[]{valid with{Quantity=0},valid with{Quantity=100001},valid with{UnitCost=null},valid with{UnitCost=-1},valid with{UnitCost=.001m},valid with{Product=product with{SellingPrice=null}},valid with{Product=product with{SellingPrice=-1}},valid with{Product=product with{Hsn="fake"}},valid with{Product=product with{TaxRateId=Guid.NewGuid()}},valid with{Product=product with{Category="Mobile"}},valid with{Product=product with{Barcode=new string('x',101)}},valid with{ProductVariantId=cable},new QuickAccessoryRequest(branch,1,50),new QuickAccessoryRequest(branch,1,50,ProductVariantId:phone)})
            Assert.Equal(HttpStatusCode.BadRequest,(await Post("/inventory/quick-accessories",input)).StatusCode);
        var request=await Post("/inventory/quick-accessories",valid with{Product=product with{Barcode="CABLE-BARCODE"}});Assert.Equal(HttpStatusCode.Conflict,request.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post("/inventory/quick-accessories",valid with{BranchId=Guid.NewGuid()})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<ProductVariant>().CountAsync());Assert.Empty(await db.Set<StockBalance>().ToArrayAsync());Assert.Empty(await db.Set<StockCostLayer>().ToArrayAsync());Assert.Empty(await db.Set<InventoryMovement>().ToArrayAsync());Assert.Empty(await db.Set<LedgerEntry>().ToArrayAsync());
    }

    [Fact]
    public async Task QuickAccessoryCategoryDefaultsAreSharedPreservedAndNeverRewriteExistingProducts()
    {
        var p=await AccessoryProduct();await Preset(new(p.Category,p.Hsn!,p.TaxRateId));var first=await QuickAccessory(new(branch,1,60,Product:new("First case",118,p.Category)));
        var zero=await Id("/tax-rates",new TaxRequest("Future category zero",0));await Preset(new(p.Category,"39269098",zero));
        var second=await QuickAccessory(new(branch,1,50,Product:new("Second case",100,p.Category)));Assert.Equal(zero,second.Product.TaxRateId);Assert.Equal("39269098",second.Product.Hsn);
        var old=(await client.GetFromJsonAsync<ProductView>($"/api/v1/products/{first.Product.Id}"))!;Assert.Equal(p.TaxRateId,old.TaxRateId);Assert.Equal(p.Hsn,old.Hsn);
        await ConfigureGst();var defaults=await client.GetFromJsonAsync<AccessoryBillingPreset[]>("/api/v1/accessory-billing-defaults");Assert.Equal(zero,Assert.Single(defaults!).TaxRateId);
        await Preset(new(p.Category,Remove:true));Assert.Empty((await client.GetFromJsonAsync<AccessoryBillingPreset[]>("/api/v1/accessory-billing-defaults"))!);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post("/inventory/quick-accessories",new QuickAccessoryRequest(branch,1,50,Product:new("Needs billing",100,p.Category)))).StatusCode);
        var settings=(await client.GetFromJsonAsync<SettingsRequest>("/api/v1/settings"))!;Assert.Equal("Smart Plaza",settings.LegalName);Assert.True(settings.GstRegistered);
    }

    [Fact]
    public async Task QuickAccessoryRespectsCreateCostBranchAndDefaultPermissions()
    {
        var p=await AccessoryProduct();await Preset(new(p.Category,p.Hsn!,p.TaxRateId));
        var response=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("quick-stock","Quick stock employee","quick-stock-test-password-2026",["inventory.view","inventory.adjust","inventory.cost.view"],[branch]));response.EnsureSuccessStatusCode();
        var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("quick-stock","quick-stock-test-password-2026"));login.EnsureSuccessStatusCode();var owner=client.DefaultRequestHeaders.Authorization;client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Single((await client.GetFromJsonAsync<AccessoryBillingPreset[]>("/api/v1/accessory-billing-defaults"))!);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/inventory/quick-accessories",new QuickAccessoryRequest(branch,1,50,Product:p))).StatusCode);
        Assert.Equal(1,(await QuickAccessory(new(branch,1,50,ProductVariantId:cable))).OnHand);
        Assert.Equal(HttpStatusCode.NotFound,(await Post("/inventory/quick-accessories",new QuickAccessoryRequest(Guid.NewGuid(),1,50,ProductVariantId:cable))).StatusCode);
        client.DefaultRequestHeaders.Authorization=owner;
        response=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("quick-create","Quick create employee","quick-create-test-password-2026",["inventory.view","inventory.adjust","inventory.cost.view","inventory.create"],[branch]));response.EnsureSuccessStatusCode();login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("quick-create","quick-create-test-password-2026"));login.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/inventory/quick-accessories",new QuickAccessoryRequest(branch,1,50,Product:p,RememberBilling:true))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(2,await db.Set<ProductVariant>().CountAsync());Assert.Equal(1,await db.Set<InventoryMovement>().CountAsync());
        var saved=await QuickAccessory(new(branch,1,50,Product:new("Staff-created case",118,p.Category)));Assert.Equal(1,saved.OnHand);
        client.DefaultRequestHeaders.Authorization=owner;response=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("quick-view","Quick viewer","quick-view-test-password-2026",["inventory.view"],[branch]));response.EnsureSuccessStatusCode();login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("quick-view","quick-view-test-password-2026"));login.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/inventory/quick-accessories",new QuickAccessoryRequest(branch,1,50,ProductVariantId:cable))).StatusCode);
        var quantities=await client.GetStringAsync($"/api/v1/inventory/quantity-availability?branchId={branch}&ids={cable},{saved.Product.Id}");Assert.DoesNotContain("cost",quantities,StringComparison.OrdinalIgnoreCase);
        var req=new HttpRequestMessage(HttpMethod.Put,"/api/v1/accessory-billing-defaults"){Content=JsonContent.Create(new AccessoryPresetRequest(p.Category,p.Hsn!,p.TaxRateId))};req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.Forbidden,(await client.SendAsync(req)).StatusCode);
    }
}
