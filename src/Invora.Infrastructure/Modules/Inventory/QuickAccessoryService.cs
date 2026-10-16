using System.Text.RegularExpressions;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Inventory;

public sealed class QuickAccessoryService(RetailOperations r, CatalogService catalog, StockService stock)
{
    public async Task<AccessoryBillingPreset[]> DefaultsAsync(CancellationToken ct)
    {
        await r.PermissionAsync("inventory.view", ct);
        return (await r.SettingsAsync(ct)).AccessoryBillingDefaults ?? [];
    }

    public Task<AccessoryBillingPreset[]> SaveDefaultAsync(AccessoryPresetRequest request, string key, CancellationToken ct) =>
        r.ExecuteAsync("accessory-billing-default", key, request, async () =>
        {
            await r.PermissionAsync("business.settings", ct);
            await SaveDefaultInternalAsync(request, ct);
            return (await r.SettingsAsync(ct)).AccessoryBillingDefaults ?? [];
        }, ct);

    private async Task SaveDefaultInternalAsync(AccessoryPresetRequest request, CancellationToken ct)
    {
        RetailOperations.Text(request.Category, "Category", 100);
        var category = request.Category.Trim();
        RetailOperations.Check(!await catalog.RequiresImeiAsync(category, ct), "Phones use individual IMEI tracking. Choose an accessory category.");
        if (!request.Remove) await ValidateBillingAsync(request.Hsn, request.TaxRateId, ct);
        var settings = await r.SettingsAsync(ct);
        var presets = (settings.AccessoryBillingDefaults ?? []).Where(x => !x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!request.Remove) presets.Add(new(category, request.Hsn.Trim(), request.TaxRateId!.Value));
        RetailOperations.Check(presets.Count <= 100, "Keep up to 100 accessory category defaults.");
        var stored = await r.Db.Set<BusinessSettings>().SingleOrDefaultAsync(ct);
        if (stored is null) { stored = new(); r.Db.Add(stored); }
        stored.Json = RetailOperations.Json(settings with { AccessoryBillingDefaults = presets.OrderBy(x => x.Category).ToArray() });
        r.Audit("ACCESSORY_BILLING_DEFAULT_UPDATED", stored.Id, request);
        // DefaultsAsync reads the saved JSON in the same transaction, including first-time settings.
        await r.Db.SaveChangesAsync(ct);
    }

    private async Task ValidateBillingAsync(string? hsn, Guid? tax, CancellationToken ct)
    {
        RetailOperations.Check(hsn is not null && Regex.IsMatch(hsn.Trim(), @"^(?:[0-9]{4}|[0-9]{6}|[0-9]{8})$"), "Enter this category's 4, 6 or 8 digit HSN code in billing details.");
        RetailOperations.Check(tax.HasValue && await r.Db.Set<TaxRate>().AnyAsync(x => x.Id == tax && x.IsActive, ct), "Choose an active tax rate in billing details.");
    }

    public Task<QuickAccessoryResult> RecordAsync(QuickAccessoryRequest request, string key, CancellationToken ct) =>
        r.ExecuteAsync("quick-accessory-stock", key, request, async () =>
        {
            await r.BranchAsync(request.BranchId, "inventory.adjust", ct);
            await r.PermissionAsync("inventory.cost.view", ct);
            await r.PermissionAsync("inventory.view", ct);
            RetailOperations.Check(request.Quantity is > 0 and <= 100000, "Enter the number of unrecorded items physically counted in this shop.");
            RetailOperations.Check(request.UnitCost.HasValue, "Enter the cost per item so profit can be calculated correctly.");
            RetailOperations.Money(request.UnitCost!.Value);
            RetailOperations.Money(request.UnitCost.Value * request.Quantity);
            RetailOperations.Check(request.ProductVariantId.HasValue != (request.Product is not null), "Choose an existing accessory or enter one new product.");
            Guid id;
            if (request.ProductVariantId is Guid existing)
            {
                RetailOperations.Check(!request.RememberBilling, "Use billing defaults to change a category. Existing products keep their billing details.");
                id = existing;
            }
            else
            {
                await r.PermissionAsync("inventory.create", ct);
                var input = request.Product!;
                RetailOperations.Text(input.Name, "Item name", 200);
                RetailOperations.Text(input.Category, "Category", 100);
                RetailOperations.Text(input.Brand, "Brand", 100);
                RetailOperations.Check(input.Barcode is null || input.Barcode.Length <= 100, "Barcode must be at most 100 characters.");
                RetailOperations.Check(!await catalog.RequiresImeiAsync(input.Category, ct), "Phones require IMEI capture. Use the phone / device form.");
                RetailOperations.Check(input.SellingPrice.HasValue, "Enter the selling price per item.");
                var normalizedName = input.Name.Trim().ToUpperInvariant();
                var normalizedCategory = input.Category.Trim().ToUpperInvariant();
                var normalizedBrand = input.Brand.Trim().ToUpperInvariant();
                RetailOperations.Check(!await (from v in r.Db.Set<ProductVariant>() join m in r.Db.Set<ProductModel>() on v.ProductModelId equals m.Id
                    where v.IsActive && m.Name.ToUpper() == normalizedName && m.Category.ToUpper() == normalizedCategory && m.Brand.ToUpper() == normalizedBrand select v.Id).AnyAsync(ct),
                    "This item already exists. Select it in the matching items list to record stock against the same product.", "PRODUCT_EXISTS");
                var preset = (await r.SettingsAsync(ct)).AccessoryBillingDefaults?.FirstOrDefault(x => x.Category.Equals(input.Category.Trim(), StringComparison.OrdinalIgnoreCase));
                var hsn = input.Hsn ?? preset?.Hsn;
                var tax = input.TaxRateId ?? preset?.TaxRateId;
                await ValidateBillingAsync(hsn, tax, ct);
                id = await catalog.CreateInternalAsync(new(input.Name, input.Brand, input.Category, hsn!.Trim(), tax!.Value,
                    string.IsNullOrWhiteSpace(input.Sku) ? "ITEM-" + Guid.NewGuid().ToString("N").ToUpperInvariant() : input.Sku, input.Barcode, "", "", input.Color ?? "", false,
                    input.SellingPrice!.Value, input.Mrp ?? input.SellingPrice.Value, input.WarrantyMonths), ct);
                await r.Db.SaveChangesAsync(ct);
                if (request.RememberBilling)
                {
                    await r.PermissionAsync("business.settings", ct);
                    await SaveDefaultInternalAsync(new(input.Category, hsn, tax), ct);
                }
            }
            var product = await catalog.ProductAsync(id, ct);
            RetailOperations.Check(!product.Variant.Serialized && !await catalog.RequiresImeiAsync(product.Model.Category, ct), "This item is tracked individually. Use the IMEI / serial stock form.");
            await stock.ReceiveAsync(request.BranchId, product.Variant, request.Quantity, request.UnitCost.Value, null, "CounterStock", null, null, ct);
            var balance = await stock.BalanceAsync(request.BranchId, id, ct);
            r.Audit("ACCESSORY_STOCK_RECORDED", id, new { request.BranchId, request.Quantity, request.UnitCost, Reason = "Previously unrecorded stock counted at counter" });
            return new QuickAccessoryResult(await catalog.ViewAsync(id, ct), request.Quantity, balance.Quantity);
        }, ct);

    public async Task<Dictionary<Guid, int>> AvailabilityAsync(Guid branch, string ids, CancellationToken ct)
    {
        await r.BranchAsync(branch, "inventory.view", ct);
        var parts = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        RetailOperations.Check(parts.Length is > 0 and <= 100 && parts.All(x => Guid.TryParse(x, out _)), "Choose up to 100 product IDs.");
        var requested = parts.Select(Guid.Parse).Distinct().ToArray();
        var products = await r.Db.Set<ProductVariant>().Where(x => requested.Contains(x.Id) && x.IsActive && !x.Serialized).Select(x => x.Id).ToArrayAsync(ct);
        var balances = await r.Db.Set<StockBalance>().Where(x => x.BranchId == branch && products.Contains(x.ProductVariantId)).ToDictionaryAsync(x => x.ProductVariantId, x => x.Quantity, ct);
        return products.ToDictionary(x => x, x => balances.GetValueOrDefault(x));
    }
}
