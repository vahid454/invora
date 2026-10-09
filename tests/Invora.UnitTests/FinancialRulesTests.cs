using Invora.Domain.Common;
using Invora.Domain.Modules.Payments;
using Invora.Domain.Modules.Taxes;
using Xunit;
namespace Invora.UnitTests;

public sealed class FinancialRulesTests
{
    [Fact]
    public void PartialReceiptAndLaterPaymentReconcile()
    {
        var entries = new[] { new LedgerAmount(30000, 0), new LedgerAmount(0, 10000), new LedgerAmount(0, 5000) };
        Assert.Equal(15000m, entries.Sum(x => x.CustomerBalanceEffect));
        Assert.Equal(15000m, PaymentRules.InvoiceDue(30000, 0, 15000));
    }
    [Fact]
    public void AdvanceReducesAccountButNotUnallocatedInvoice()
    {
        Assert.Equal(12000m, new[] { new LedgerAmount(30000, 0), new LedgerAmount(0, 18000) }.Sum(x => x.CustomerBalanceEffect));
        Assert.Equal(15000m, PaymentRules.InvoiceDue(30000, 0, 15000));
    }
    [Fact] public void ReversalRestoresBalance() => Assert.Equal(0m, new LedgerAmount(0, 5000).CustomerBalanceEffect + new LedgerAmount(0, 5000).Reverse().CustomerBalanceEffect);
    [Theory]
    [InlineData(100, 80, 100, 30)]
    [InlineData(100, 0, 20, 30)]
    public void OverAllocationIsRejected(decimal receipt, decimal allocated, decimal due, decimal amount) => Assert.Throws<DomainException>(() => PaymentRules.ValidateAllocation(receipt, allocated, due, amount));
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(1.001, 0)]
    public void InvalidLedgerIsRejected(decimal debit, decimal credit) => Assert.Throws<DomainException>(() => new LedgerAmount(debit, credit));
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InclusiveTaxAlwaysReconciles(bool interstate)
    {
        for (var cents = 1; cents < 10000; cents += 7)
        {
            var gross = cents / 100m;
            var total = InvoiceCalculator.Calculate([new(1, gross, 0, 18, true)], interstate);
            Assert.Equal(gross, total.Total);
            Assert.Equal(total.Total, total.TaxableValue + total.Cgst + total.Sgst + total.Igst + total.RoundOff);
            Assert.Equal(0m, interstate ? total.Cgst + total.Sgst : total.Igst);
        }
    }
    [Fact]
    public void DiscountPrecedesExclusiveTax()
    {
        var total = InvoiceCalculator.Calculate([new(2, 100, 20, 18, false)], false);
        Assert.Equal(180m, total.TaxableValue); Assert.Equal(16.20m, total.Cgst); Assert.Equal(212.40m, total.Total);
    }
    [Fact] public void DiscountCannotExceedLine() => Assert.Throws<DomainException>(() => InvoiceCalculator.Calculate([new(1, 100, 101, 18, false)], false));
}
