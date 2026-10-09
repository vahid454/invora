using Invora.Domain.Common;
namespace Invora.Domain.Modules.Inventory;

public enum InventoryStatus { InStock = 1, Sold = 2, InRepair = 3, Damaged = 4, InTransit = 5 }
public enum ReturnDisposition { Restock = 1, Repair = 2, Damaged = 3 }
public sealed class InventoryUnit : Entity
{
    private InventoryUnit() { }
    public Guid BranchId { get; private set; }
    public string Imei { get; private set; } = "";
    public decimal EffectiveCost { get; private set; }
    public InventoryStatus Status { get; private set; } = InventoryStatus.InStock;
    public Guid? ActiveSaleItemId { get; private set; }
    public static InventoryUnit Receive(Guid branchId, string imei, decimal effectiveCost)
    {
        if (branchId == Guid.Empty) throw new ArgumentException("Branch is required.", nameof(branchId));
        if (imei.Length != 15 || imei.Any(c => c < '0' || c > '9')) throw new DomainException("INVALID_IMEI", "IMEI must contain 15 ASCII digits.");
        if (effectiveCost < 0 || decimal.Round(effectiveCost, 2) != effectiveCost) throw new DomainException("INVALID_COST", "Cost must be nonnegative with at most two decimals.");
        return new() { BranchId = branchId, Imei = imei, EffectiveCost = effectiveCost };
    }
    public void Sell(Guid branchId, Guid saleItemId)
    {
        if (BranchId != branchId) throw new DomainException("BRANCH_MISMATCH", "Device belongs to another branch.");
        if (Status != InventoryStatus.InStock) throw new DomainException("INVENTORY_UNAVAILABLE", "Device is not in stock.");
        if (saleItemId == Guid.Empty) throw new ArgumentException("Sale item is required.", nameof(saleItemId));
        Status = InventoryStatus.Sold; ActiveSaleItemId = saleItemId; Version++;
    }
    public void Return(Guid originalSaleItemId, ReturnDisposition disposition)
    {
        if (Status != InventoryStatus.Sold || ActiveSaleItemId != originalSaleItemId)
            throw new DomainException("INVALID_RETURN", "Return must reference the active sale of this device.");
        Status = disposition switch { ReturnDisposition.Restock => InventoryStatus.InStock, ReturnDisposition.Repair => InventoryStatus.InRepair, ReturnDisposition.Damaged => InventoryStatus.Damaged, _ => throw new DomainException("INVALID_DISPOSITION", "Unknown return disposition.") };
        ActiveSaleItemId = null; Version++;
    }
}
