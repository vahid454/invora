using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Retail;
using Invora.Infrastructure.Pdf;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Inventory;

public sealed record ItemLabelView(Guid Id, string Kind, string Code, string Model, string Details,
    string Category, string Condition, string Sku, string[] Identifiers, string QrPng);

public sealed class ItemLabelService(RetailOperations r, CatalogService catalog)
{
    public async Task<ItemLabelView[]> LabelsAsync(Guid branchId, string kind, string ids, CancellationToken ct)
    {
        await r.BranchAsync(branchId, "inventory.view", ct);
        RetailOperations.Check(kind is "unit" or "product", "Choose device or quantity-product labels.");
        RetailOperations.Text(ids, "Selected items", 1900);
        var selected = ids.Split(',');
        RetailOperations.Check(selected.Length is > 0 and <= 50, "Select 1–50 items at a time.");
        var keys = new List<Guid>();
        foreach (var value in selected)
        {
            RetailOperations.Check(Guid.TryParse(value, out var id) && id != Guid.Empty, "Invalid selected item.");
            RetailOperations.Check(!keys.Contains(id), "Select each item only once; use copies for reprints.");
            keys.Add(id);
        }

        var result = new List<ItemLabelView>();
        foreach (var id in keys)
        {
            StockUnit? unit = null;
            if (kind == "unit") unit = await r.Db.Set<StockUnit>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == branchId, ct)
                ?? throw new DomainException("NOT_FOUND", "Device is not in this branch.");
            var product = await catalog.ProductAsync(unit?.ProductVariantId ?? id, ct);
            RetailOperations.Check(unit is not null || !product.Variant.Serialized,
                "This product needs an individual label for each device. Open Inventory → Devices.");
            var identifiers = unit is null ? Array.Empty<string>() : await r.Db.Set<UnitIdentifier>()
                .Where(x => x.StockUnitId == id).OrderBy(x => x.Slot)
                .Select(x => x.Slot + ": " + x.Value).ToArrayAsync(ct);
            var code = new ItemLabelCode(kind, id).Value;
            result.Add(new(id, kind, code, product.Model.Brand + " " + product.Model.Name,
                string.Join(" · ", new[] { product.Variant.Ram, product.Variant.Storage, product.Variant.Color }
                    .Where(x => !string.IsNullOrWhiteSpace(x))), product.Model.Category, unit?.Condition ?? "",
                product.Variant.Sku, identifiers, ItemLabelPdf.QrPng(code)));
        }
        return result.ToArray();
    }
}
