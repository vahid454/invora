using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Common;
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
    private Task<JsonElement> ValuationSummary(Guid? selected = null) => client.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/summary?branchId={selected ?? branch}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
    private async Task<PageResponse<InventoryView>> ValuationDevices(Guid? selected = null) => (await client.GetFromJsonAsync<PageResponse<InventoryView>>($"/api/v1/inventory?branchId={selected ?? branch}"))!;
    private async Task<Guid> TaxedProduct(bool serialized)
    {
        var tax = await Id("/tax-rates", new TaxRequest("GST 18", 18));
        return await Id("/products", new ProductRequest("Valuation product", "Samsung", serialized ? "Mobile" : "Accessories", "85171300", tax, serialized ? "VALUE-PHONE" : "VALUE-CABLE", null, "", "", "", serialized, 90000, 100000));
    }
    private async Task<Guid> ValuationBranch(string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/branches", new BranchRequest(code, code));
        response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<BranchView>())!.Id;
    }

    [Fact]
    public async Task InventoryPurchaseValueShows84999IncludingTaxWithoutChangingProfitOrPostedMoney()
    {
        await ConfigureGst();phone = await TaxedProduct(true);
        await Purchase([new(phone, 1, 84999, TaxInclusive: true, Devices: [new("740000000000001")])]);
        var unit = Assert.Single((await ValuationDevices()).Items);
        Assert.Equal(84999, unit.PurchaseValue);Assert.Equal(72033.05m, unit.Cost);
        var summary = await ValuationSummary();Assert.Equal(84999, summary.GetProperty("stockPurchaseValue").Amount());Assert.Equal(72033.05m, summary.GetProperty("stockValue").Amount());
        Assert.Equal(84999, summary.GetProperty("supplierPayable").Amount());Assert.Equal(0, summary.GetProperty("grossProfit").Amount());
        var csv = await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}");Assert.Contains("Purchase value (incl. tax)", csv);Assert.Contains("84999.00", csv);Assert.Contains("72033.05", csv);
        var aging = await client.GetFromJsonAsync<PageResponse<InventoryView>>($"/api/v1/inventory?aging=true&branchId={branch}");Assert.Equal(84999, Assert.Single(aging!.Items).PurchaseValue);
        var otherPeriod = await client.GetFromJsonAsync<JsonElement>($"/api/v1/dashboard/summary?branchId={branch}&from=2025-01-01&to=2025-01-01");Assert.Equal(84999, otherPeriod.GetProperty("stockPurchaseValue").Amount());

        // Product/tax edits must never revalue a posted receipt.
        var zero = await Id("/tax-rates", new TaxRequest("Future zero", 0));
        var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{phone}") {Content=JsonContent.Create(new ProductRequest("Valuation product", "Samsung", "Mobile", "85171300", zero, "VALUE-PHONE", null, "", "", "", true, 90000, 100000))};
        update.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());(await client.SendAsync(update)).EnsureSuccessStatusCode();
        Assert.Equal(84999, Assert.Single((await ValuationDevices()).Items).PurchaseValue);
        var draft = await Document("/sales", new SaleRequest(branch, customer, date, null, false, [new(phone, 1, 90000, unit.Id)], [new(90000, 1)]));
        await Document($"/sales/{draft.Id}/complete", new {});
        summary = await ValuationSummary();Assert.Equal(0, summary.GetProperty("stockPurchaseValue").Amount());Assert.Equal(17966.95m, summary.GetProperty("grossProfit").Amount());
        await using var scope = factory.Services.CreateAsyncScope();var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Equal(72033.05m, (await db.Set<SaleItem>().SingleAsync()).Cost);Assert.Equal(84999, (await db.Set<PurchaseItem>().SingleAsync()).Total);Assert.Equal(72033.05m, (await db.Set<StockUnit>().SingleAsync()).Cost);
        Assert.Equal(2, await db.Set<LedgerEntry>().CountAsync(x=>x.Kind=="Purchase"||x.Kind=="Sale"));
    }

    [Fact]
    public async Task InventoryPurchaseValuePreservesDiscountRoundingThroughCorrectionSaleReturnAndSupplierReturn()
    {
        await ConfigureGst();phone = await TaxedProduct(true);
        var purchase = await Purchase([new(phone, 3, 33.34m, Discount: .01m, TaxInclusive: true, Devices: [new("740000000000002"), new("740000000000003"), new("740000000000004")])]);
        var original = (await ValuationDevices()).Items;Assert.Equal(100.01m, original.Sum(x=>x.PurchaseValue));Assert.Equal(new[]{33.33m,33.34m,33.34m},original.Select(x=>x.PurchaseValue!.Value).Order().ToArray());
        var unit = original.First();await Id($"/inventory/{unit.Id}/corrections", Correction(await DeviceDetail(unit.Id)));
        var changed = (await ValuationDevices()).Items.Single(x=>x.Id==unit.Id);Assert.Equal(unit.PurchaseValue,changed.PurchaseValue);
        var draft = await Document("/sales", new SaleRequest(branch, customer, date, date.AddDays(7), false, [new(changed.ProductVariantId, 1, 50, unit.Id)], []));var sale=await Document($"/sales/{draft.Id}/complete",new{});
        Assert.Equal(100.01m-unit.PurchaseValue, (await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var item=await db.Set<SaleItem>().SingleAsync();var bill=await db.Set<PurchaseItem>().SingleAsync();
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Restock",date,[new(item.Id,1,"Restock")]));
        Assert.Equal(100.01m,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());Assert.Equal(0,(await ValuationSummary()).GetProperty("grossProfit").Amount());
        await Id($"/purchases/{purchase.Id}/returns",new PurchaseReturnRequest("Return to supplier","VALUE-RETURN",date,[new(bill.Id,1,unit.Id)]));
        Assert.Equal(100.01m-unit.PurchaseValue,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());
        foreach(var row in (await ValuationDevices()).Items)Assert.Equal(original.Single(x=>x.Id==row.Id).PurchaseValue,row.PurchaseValue);
        Assert.Equal(bill.Taxable,await db.Set<StockUnit>().SumAsync(x=>x.Cost));
    }

    [Fact]
    public async Task InventoryPurchaseValueFollowsMultiHopQuantityTransfersPartialCancellationAndReturns()
    {
        await ConfigureGst();cable=await TaxedProduct(false);
        var purchase=await Purchase([new(cable,5,118,TaxInclusive:true)]);var second=await ValuationBranch("SECOND");var third=await ValuationBranch("THIRD");
        var transfer=await Document("/stock-transfers",new TransferRequest(branch,second,"Partial receipt",[new(cable,3)]));
        Assert.Equal(236,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var transferItem=await db.Set<StockTransferItem>().SingleAsync();
        await Document($"/stock-transfers/{transfer.Id}/receive",new TransferReceiptRequest([new(transferItem.Id,1)]));
        await Document($"/stock-transfers/{transfer.Id}/cancel",new ReasonRequest("Two units returned to source"));
        Assert.Equal(472,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());Assert.Equal(118,(await ValuationSummary(second)).GetProperty("stockPurchaseValue").Amount());
        var onward=await Document("/stock-transfers",new TransferRequest(second,third,"Second hop",[new(cable,1)]));await Document($"/stock-transfers/{onward.Id}/receive",new{});
        Assert.Equal(0,(await ValuationSummary(second)).GetProperty("stockPurchaseValue").Amount());Assert.Equal(118,(await ValuationSummary(third)).GetProperty("stockPurchaseValue").Amount());
        var quantities=await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/balances?branchId={third}");var row=Assert.Single(quantities.GetProperty("items").EnumerateArray());Assert.Equal(118,row.GetProperty("purchaseValue").Amount());Assert.Equal(100,row.GetProperty("cost").Amount());
        var csv=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={third}&view=quantity");Assert.Contains("Stock purchase value (incl. tax)",csv);Assert.Contains("118.00",csv);Assert.Contains("100.00",csv);
        var sale=await Document("/sales",new SaleRequest(third,customer,date,date.AddDays(7),false,[new(cable,1,200)],[]));await Document($"/sales/{sale.Id}/complete",new{});
        Assert.Equal(0,(await ValuationSummary(third)).GetProperty("stockPurchaseValue").Amount());var saleItem=await db.Set<SaleItem>().SingleAsync();
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("Returned at destination",date,[new(saleItem.Id,1,"Restock")]));Assert.Equal(118,(await ValuationSummary(third)).GetProperty("stockPurchaseValue").Amount());
        var bill=await db.Set<PurchaseItem>().SingleAsync();await Id($"/purchases/{purchase.Id}/returns",new PurchaseReturnRequest("Supplier return","Q-VALUE-RETURN",date,[new(bill.Id,1)]));
        Assert.Equal(354,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());Assert.Equal(400,await db.Set<StockCostLayer>().AsNoTracking().SumAsync(x=>x.RemainingQuantity*x.UnitCost));
        var branches=await client.GetFromJsonAsync<JsonElement>($"/api/v1/reports/branches?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");Assert.Equal(472,branches.EnumerateArray().Sum(x=>x.GetProperty("summary").GetProperty("stockPurchaseValue").Amount()));
    }

    [Fact]
    public async Task InventoryPurchaseValueUsesCurrentUsedAcquisitionAndOpeningAmounts()
    {
        await ConfigureGst();phone=await TaxedProduct(true);
        await Purchase([new(phone,1,1180,TaxInclusive:true,Devices:[new("740000000000005")])]);var unit=await Unit();await Sale(unit.Id);
        await Id("/device-acquisitions",new AcquisitionRequest(branch,customer,phone,new("740000000000005"),700,1,"Used","Bought back",80,0));
        var current=Assert.Single((await ValuationDevices()).Items);Assert.Equal(700,current.PurchaseValue);Assert.Equal(700,current.Cost);
        await Id("/inventory/adjustments",new AdjustmentRequest(branch,cable,2,50,"Opening stock"));
        Assert.Equal(800,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());Assert.Equal(800,(await ValuationSummary()).GetProperty("stockValue").Amount());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.NotNull((await db.Set<StockUnit>().SingleAsync()).PurchaseItemId);Assert.Equal(1180,(await db.Set<PurchaseItem>().SingleAsync()).Total);
    }

    [Fact]
    public async Task InventoryPurchaseValuesAreHiddenWithoutCostPermissionIncludingAgingQuantityAndExports()
    {
        await Purchase([new(phone,1,1000,Devices:[new("740000000000006")]),new(cable,2,100)]);
        var response=await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("value-viewer","Value viewer","value-viewer-password-2026",["inventory.view","reports.view","reports.export"],[branch]));response.EnsureSuccessStatusCode();
        var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("value-viewer","value-viewer-password-2026"));login.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        foreach(var path in new[]{"/inventory?","/inventory?aging=true&","/inventory/balances?"})
        {
            var data=await client.GetFromJsonAsync<JsonElement>($"/api/v1{path}branchId={branch}");foreach(var item in data.GetProperty("items").EnumerateArray()){Assert.Equal(JsonValueKind.Null,item.GetProperty("purchaseValue").ValueKind);Assert.Equal(JsonValueKind.Null,item.GetProperty("cost").ValueKind);}
        }
        var summary=await ValuationSummary();Assert.Equal(JsonValueKind.Null,summary.GetProperty("stockPurchaseValue").ValueKind);Assert.Equal(JsonValueKind.Null,summary.GetProperty("stockValue").ValueKind);
        foreach(var view in new[]{"devices","aging","quantity"}){var csv=await client.GetStringAsync($"/api/v1/exports/inventory?branchId={branch}&view={view}");Assert.DoesNotContain("Purchase value",csv,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("cost for profit",csv,StringComparison.OrdinalIgnoreCase);}
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/dashboard/summary?branchId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task InventoryPurchaseValueHandlesExclusiveTaxDiscountsNonGstCostAndFractionalQuantityProration()
    {
        // Default setup is not GST registered, so the stored book cost already includes tax.
        phone=await TaxedProduct(true);
        await Purchase([new(phone,1,1100,Discount:100,Devices:[new("740000000000007")]),new(cable,3,.01m,Discount:.01m)]);
        var unit=Assert.Single((await ValuationDevices()).Items);Assert.Equal(1180,unit.PurchaseValue);Assert.Equal(1180,unit.Cost);
        Assert.Equal(1180.02m,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());
        var destination=await ValuationBranch("PHONEDEST");var transfer=await Document("/stock-transfers",new TransferRequest(branch,destination,"Phone transfer",[new(phone,1,unit.Id)]));
        Assert.Equal(.02m,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());Assert.Equal(0,(await ValuationSummary(destination)).GetProperty("stockPurchaseValue").Amount());
        await Document($"/stock-transfers/{transfer.Id}/receive",new{});Assert.Equal(1180,(await ValuationSummary(destination)).GetProperty("stockPurchaseValue").Amount());Assert.Equal(1180,Assert.Single((await ValuationDevices(destination)).Items).PurchaseValue);
        // A two-paise line shared by three quantity units is prorated before rounding.
        var sale=await Sale(null,2);Assert.Equal(.01m,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();var line=await db.Set<SaleItem>().SingleAsync();
        await Document($"/sales/{sale.Id}/returns",new ReturnRequest("All accessories returned",date,[new(line.Id,2,"Restock")]));
        Assert.Equal(.02m,(await ValuationSummary()).GetProperty("stockPurchaseValue").Amount());Assert.Equal(.02m,await db.Set<StockCostLayer>().AsNoTracking().SumAsync(x=>x.RemainingQuantity*x.UnitCost));
    }
}
