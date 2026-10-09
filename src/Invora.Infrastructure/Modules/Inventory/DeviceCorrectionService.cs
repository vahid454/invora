using System.Security.Cryptography;
using System.Text;
using Invora.Contracts.Retail;
using Invora.Domain.Common;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Catalog;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Inventory;

public sealed class DeviceCorrectionService(RetailOperations r, CatalogService catalog, StockService stock)
{
    private async Task<StockUnit> UnitAsync(Guid id, Guid branch, string permission, CancellationToken ct)
    {
        await r.BranchAsync(branch, permission, ct);
        return await r.Db.Set<StockUnit>().SingleOrDefaultAsync(x => x.Id == id && x.BranchId == branch, ct)
            ?? throw new DomainException("NOT_FOUND", "Device not found in this branch.");
    }

    private async Task<DeviceInspection?> InspectionAsync(Guid id, CancellationToken ct) =>
        await r.Db.Set<DeviceInspection>().Where(x => x.StockUnitId == id)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<DeviceEditState> StateAsync(StockUnit unit, DeviceInspection? inspection, CancellationToken ct)
    {
        var p = await catalog.ProductAsync(unit.ProductVariantId, ct);
        return new(p.Variant.Ram, p.Variant.Storage, p.Variant.Color, unit.Condition, inspection?.BatteryHealth, inspection?.Notes ?? "");
    }

    private async Task<string> RevisionAsync(StockUnit unit, DeviceInspection? inspection, CancellationToken ct)
    {
        var correction = await r.Db.Set<DeviceCorrection>().Where(x => x.StockUnitId == unit.Id)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var movement = await r.Db.Set<InventoryMovement>().Where(x => x.StockUnitId == unit.Id)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RetailOperations.Json(new
        { unit.Id, unit.BranchId, unit.ProductVariantId, unit.Status, unit.Condition, unit.ReceivedAtUtc, inspection = inspection?.Id, correction, movement }))));
    }

    public async Task<object> DetailAsync(Guid id, Guid branch, CancellationToken ct)
    {
        await using var snapshot = await r.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var unit = await UnitAsync(id, branch, "inventory.view", ct);
        var inspection = await InspectionAsync(id, ct);
        var history = await (from entry in r.Db.Set<DeviceCorrection>().AsNoTracking()
                             join actor in r.Db.StaffUsers on entry.ActorId equals actor.Id
                             where entry.StockUnitId == id && entry.BranchId == branch
                             orderby entry.CreatedAtUtc descending, entry.Id descending
                             select new { entry.Id, entry.CreatedAtUtc, actor = actor.DisplayName, entry.Reason, entry.BeforeJson, entry.AfterJson })
            .Take(100).ToArrayAsync(ct);
        return new
        {
            unit = await stock.ViewAsync(unit, ct), current = await StateAsync(unit, inspection, ct),
            revision = await RevisionAsync(unit, inspection, ct), canEdit = r.Can("inventory.adjust") && unit.Status == DeviceStatus.InStock,
            history = history.Select(x => new { x.Id, x.CreatedAtUtc, x.actor, x.Reason, before = RetailOperations.Read<DeviceEditState>(x.BeforeJson), after = RetailOperations.Read<DeviceEditState>(x.AfterJson) })
        };
    }

    public Task<Guid> CorrectAsync(Guid id, DeviceCorrectionRequest request, string key, CancellationToken ct) =>
        r.ExecuteAsync("device-correction", key, new { id, request }, async () =>
        {
            var unit = await UnitAsync(id, request.BranchId, "inventory.adjust", ct);
            RetailOperations.Check(unit.Status == DeviceStatus.InStock, "Only available stock can be corrected. Sold or transferred devices must use their return / receipt workflow.", "INVENTORY_UNAVAILABLE");
            RetailOperations.Text(request.Reason, "Correction reason", 1000);
            RetailOperations.Check((request.Ram?.Length ?? 0) <= 50 && (request.Storage?.Length ?? 0) <= 50 && (request.Color?.Length ?? 0) <= 100 && (request.Notes?.Length ?? 0) <= 2000, "Device details are too long.");
            RetailOperations.Check(request.BatteryHealth is null or >= 0 and <= 100, "Battery health must be 0–100, or left blank if unknown.");
            RetailOperations.Check(unit.Condition == "New" ? request.Condition == "New" : request.Condition is "Used" or "Refurbished" or "OpenBox", "Keep the device's original new / used classification.");
            var inspection = await InspectionAsync(id, ct);
            RetailOperations.Check(request.Revision == await RevisionAsync(unit, inspection, ct), "This device changed since you opened it. Reload its details before saving.", "STALE_DEVICE");
            var before = await StateAsync(unit, inspection, ct);
            var after = new DeviceEditState((request.Ram ?? "").Trim(), (request.Storage ?? "").Trim(), (request.Color ?? "").Trim(), request.Condition, request.BatteryHealth, (request.Notes ?? "").Trim());
            RetailOperations.Check(before != after, "Change a device detail before saving.");
            var product = await catalog.ProductAsync(unit.ProductVariantId, ct);
            var variant = await catalog.DeviceVariantAsync(product.Variant, product.Model,
                new(new(null), after.Ram, after.Storage, after.Color, unit.Cost, product.Variant.SellingPrice, product.Variant.Mrp), ct, correction: true);
            after = after with { Ram = variant.Ram, Storage = variant.Storage, Color = variant.Color };
            RetailOperations.Check(before != after, "Change a device detail before saving.");
            unit.ProductVariantId = variant.Id;
            unit.Condition = after.Condition;
            r.Db.Add(new DeviceInspection { StockUnitId = id, SellerName = inspection?.SellerName ?? "", AadhaarLastFour = inspection?.AadhaarLastFour ?? "", ChecksJson = inspection?.ChecksJson ?? "{}", Notes = after.Notes, BatteryHealth = after.BatteryHealth, WarrantyDays = 0 });
            var change = new DeviceCorrection { StockUnitId = id, BranchId = request.BranchId, ActorId = r.Actor, Reason = request.Reason.Trim(), BeforeJson = RetailOperations.Json(before), AfterJson = RetailOperations.Json(after) };
            r.Db.Add(change);
            r.Audit("DEVICE_DETAILS_CORRECTED", id, new { correctionId = change.Id, change.Reason, before, after });
            return change.Id;
        }, ct);
}
