using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Inventory;

/// <summary>Read-only purchase amounts, independent of the stored costs used for profit.</summary>
public sealed class InventoryValuationService(RetailOperations r)
{
    public async Task<Dictionary<Guid, decimal>> UnitsAsync(IReadOnlyCollection<StockUnit> units, CancellationToken ct)
    {
        var values = units.ToDictionary(x => x.Id, x => x.Cost);
        // A phone bought back from a customer keeps its old PurchaseItemId for history.
        // Its new acquisition amount, rather than that old supplier bill, is its value now.
        var purchased = units.Where(x => x.Source == "Purchase" && x.PurchaseItemId.HasValue).ToArray();
        var ids = purchased.Select(x => x.PurchaseItemId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return values;
        var bills = await r.Db.Set<PurchaseItem>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        // Include sold, returned and reacquired units so a remaining phone never changes rank.
        var originals = await r.Db.Set<StockUnit>().AsNoTracking()
            .Where(x => x.PurchaseItemId.HasValue && ids.Contains(x.PurchaseItemId.Value))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.PurchaseItemId }).ToArrayAsync(ct);
        var ranks = originals.GroupBy(x => x.PurchaseItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Select((x, index) => (x.Id, index)).ToDictionary(x => x.Id, x => x.index));
        foreach (var unit in purchased)
        {
            var id = unit.PurchaseItemId!.Value;
            if (!bills.TryGetValue(id, out var bill) || bill.Quantity <= 0 || ranks[id].Count != bill.Quantity)
                throw ReconciliationError();
            var baseValue = decimal.Floor(bill.Total / bill.Quantity * 100) / 100;
            var extra = (int)((bill.Total - baseValue * bill.Quantity) * 100);
            values[unit.Id] = baseValue + (ranks[id][unit.Id] < extra ? 0.01m : 0);
        }
        return values;
    }

    public async Task<Dictionary<Guid, decimal>> QuantitiesAsync(IReadOnlyCollection<StockCostLayer> layers, CancellationToken ct)
    {
        // Transfers create descendants of FIFO layers, possibly across several branches.
        var ancestry = layers.ToDictionary(x => x.Id);
        while (true)
        {
            var missing = ancestry.Values.Where(x => x.OriginLayerId.HasValue && !ancestry.ContainsKey(x.OriginLayerId.Value))
                .Select(x => x.OriginLayerId!.Value).Distinct().ToArray();
            if (missing.Length == 0) break;
            var parents = await r.Db.Set<StockCostLayer>().AsNoTracking().Where(x => missing.Contains(x.Id)).ToArrayAsync(ct);
            if (parents.Length != missing.Length) throw ReconciliationError();
            foreach (var parent in parents) ancestry.Add(parent.Id, parent);
        }
        var roots = new Dictionary<Guid, StockCostLayer>();
        foreach (var layer in layers)
        {
            var root = layer;
            var visited = new HashSet<Guid> { root.Id };
            while (root.OriginLayerId is Guid parent)
            {
                if (!visited.Add(parent)) throw ReconciliationError();
                root = ancestry[parent];
            }
            roots.Add(layer.Id, root);
        }
        var ids = roots.Values.Where(x => x.PurchaseItemId.HasValue).Select(x => x.PurchaseItemId!.Value).Distinct().ToArray();
        var bills = await r.Db.Set<PurchaseItem>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var values = new Dictionary<Guid, decimal>();
        foreach (var layer in layers)
        {
            var root = roots[layer.Id];
            var unitValue = root.UnitCost;
            if (root.PurchaseItemId is Guid id)
            {
                if (!bills.TryGetValue(id, out var bill) || bill.Quantity <= 0) throw ReconciliationError();
                unitValue = bill.Total / bill.Quantity;
            }
            values[layer.ProductVariantId] = values.GetValueOrDefault(layer.ProductVariantId) + unitValue * layer.RemainingQuantity;
        }
        // Round once per product/branch, after proration; do not change FIFO book costs.
        return values.ToDictionary(x => x.Key, x => decimal.Round(x.Value, 2, MidpointRounding.AwayFromZero));
    }

    private static DomainException ReconciliationError() => new("STOCK_RECONCILIATION_FAILED", "Stock purchase history could not be reconciled. Please review the original receipt.");
}
