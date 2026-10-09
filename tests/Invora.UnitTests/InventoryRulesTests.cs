using Invora.Domain.Common;
using Invora.Domain.Modules.Inventory;
using Xunit;
namespace Invora.UnitTests;

public sealed class InventoryRulesTests
{
    [Fact]
    public void DoubleSaleIsRejected()
    {
        var branch = Guid.NewGuid(); var unit = InventoryUnit.Receive(branch, "000000000000001", 100);
        unit.Sell(branch, Guid.NewGuid()); Assert.Throws<DomainException>(() => unit.Sell(branch, Guid.NewGuid()));
    }
    [Fact]
    public void ReturnPreservesDeviceAndAllowsResaleOnlyAfterRestock()
    {
        var branch = Guid.NewGuid(); var sale = Guid.NewGuid(); var unit = InventoryUnit.Receive(branch, "000000000000001", 100);
        unit.Sell(branch, sale); unit.Return(sale, ReturnDisposition.Restock); unit.Sell(branch, Guid.NewGuid());
        Assert.Equal(InventoryStatus.Sold, unit.Status); Assert.Equal(100m, unit.EffectiveCost);
    }
    [Fact]
    public void DamagedReturnCannotBeResold()
    {
        var branch = Guid.NewGuid(); var sale = Guid.NewGuid(); var unit = InventoryUnit.Receive(branch, "000000000000001", 100);
        unit.Sell(branch, sale); unit.Return(sale, ReturnDisposition.Damaged);
        Assert.Throws<DomainException>(() => unit.Sell(branch, Guid.NewGuid()));
    }
    [Fact]
    public void WrongOriginalSaleCannotReturnDevice()
    {
        var branch = Guid.NewGuid(); var unit = InventoryUnit.Receive(branch, "000000000000001", 100); unit.Sell(branch, Guid.NewGuid());
        Assert.Throws<DomainException>(() => unit.Return(Guid.NewGuid(), ReturnDisposition.Restock));
    }
    [Fact] public void WrongBranchCannotSellDevice() => Assert.Throws<DomainException>(() => InventoryUnit.Receive(Guid.NewGuid(), "000000000000001", 100).Sell(Guid.NewGuid(), Guid.NewGuid()));
}
