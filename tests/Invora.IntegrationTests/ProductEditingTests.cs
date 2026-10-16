using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Invora.Infrastructure.Modules.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    [Fact]
    public async Task ProductEditsPreservePostedInvoicesStockCostsAndStableLabels()
    {
        await ConfigureGst();
        var input = (await AccessoryProduct()) with { Sku = "CASE-CUSTOM", Color = "White", Barcode = "CASE-OLD" };
        var created = await QuickAccessory(new(branch, 3, 60, Product: input));
        Assert.Equal("CASE-CUSTOM", created.Product.Sku);
        Assert.Equal("White", created.Product.Color);
        var draft = await Document("/sales", new SaleRequest(branch, customer, date, null, false, [new(created.Product.Id, 1, 118)], [new(118, 1)]));
        var sale = await Document($"/sales/{draft.Id}/complete", new { });
        var invoiceBefore = await client.GetStringAsync($"/api/v1/invoices/{sale.Id}");
        var labelsBefore = (await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("product", created.Product.Id)))!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        var movements = await db.Set<InventoryMovement>().CountAsync();
        var ledger = await db.Set<LedgerEntry>().CountAsync();
        var tax = await Id("/tax-rates", new TaxRequest("Edited category zero", 0));
        var edit = new ProductRequest("Clear protective case", "Case Brand", "Phone covers", "39269090", tax, created.Product.Sku,
            "CASE-NEW", "", "", "Clear", false, 150, 180, 6);
        async Task<HttpResponseMessage> Put(ProductRequest body)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/products/" + created.Product.Id) { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return await client.SendAsync(request);
        }
        (await Put(edit)).EnsureSuccessStatusCode();
        var saved = (await client.GetFromJsonAsync<ProductView>("/api/v1/products/" + created.Product.Id))!;
        Assert.Equal(edit.Name, saved.Name); Assert.Equal(edit.Brand, saved.Brand); Assert.Equal(edit.Category, saved.Category);
        Assert.Equal(edit.Hsn, saved.Hsn); Assert.Equal(tax, saved.TaxRateId); Assert.Equal("Clear", saved.Color);
        Assert.Equal(150, saved.SellingPrice); Assert.Equal(180, saved.Mrp); Assert.Equal(6, saved.WarrantyMonths);
        Assert.Equal(invoiceBefore, await client.GetStringAsync($"/api/v1/invoices/{sale.Id}"));
        Assert.Equal(2, (await db.Set<StockBalance>().AsNoTracking().SingleAsync()).Quantity);
        Assert.Equal(120, await db.Set<StockCostLayer>().SumAsync(x => x.UnitCost * x.RemainingQuantity));
        Assert.Equal(movements, await db.Set<InventoryMovement>().CountAsync()); Assert.Equal(ledger, await db.Set<LedgerEntry>().CountAsync());
        var labelsAfter = (await client.GetFromJsonAsync<ItemLabelView[]>(LabelPath("product", created.Product.Id)))!;
        Assert.Equal(labelsBefore[0].Code, labelsAfter[0].Code);
        Assert.Equal(created.Product.Id, (await ScanLabel(labelsBefore[0].Code)).GetProperty("productVariantId").GetGuid());
        var audit = await db.AccessAudits.SingleAsync(x => x.Action == "PRODUCT_UPDATED" && x.ResourceId == created.Product.Id);
        var before = JsonDocument.Parse(audit.DetailsJson).RootElement.GetProperty("before");
        Assert.Equal(input.Hsn, before.GetProperty("Hsn").GetString()); Assert.Equal("White", before.GetProperty("Color").GetString());
        Assert.Equal("CASE-OLD", before.GetProperty("Barcode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(edit with { Sku = "REPLACED" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(edit with { Serialized = true })).StatusCode);
        Assert.Equal(edit.Name, (await client.GetFromJsonAsync<ProductView>("/api/v1/products/" + created.Product.Id))!.Name);
    }
}
