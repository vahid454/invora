using Invora.Domain.Common;
namespace Invora.Domain.Modules.Payments;

public static class PaymentRules
{
    public static void ValidateAllocation(decimal paymentAmount, decimal alreadyAllocated, decimal invoiceDue, decimal allocation)
    {
        if (paymentAmount <= 0 || alreadyAllocated < 0 || invoiceDue < 0 || allocation <= 0 || new[] { paymentAmount, alreadyAllocated, invoiceDue, allocation }.Any(x => decimal.Round(x, 2) != x))
            throw new DomainException("INVALID_PAYMENT_AMOUNT", "Payment amounts must be positive two-decimal values.");
        if (alreadyAllocated + allocation > paymentAmount) throw new DomainException("PAYMENT_OVERALLOCATED", "Allocation exceeds available receipt amount.");
        if (allocation > invoiceDue) throw new DomainException("INVOICE_OVERALLOCATED", "Allocation exceeds invoice due.");
    }
    public static decimal InvoiceDue(decimal total, decimal credits, decimal activeAllocations)
    {
        if (total < 0 || credits < 0 || activeAllocations < 0 || credits + activeAllocations > total)
            throw new DomainException("INVALID_SETTLEMENT", "Invoice credits and allocations do not reconcile.");
        return total - credits - activeAllocations;
    }
}
public sealed record LedgerAmount
{
    public decimal Debit { get; }
    public decimal Credit { get; }
    public LedgerAmount(decimal debit, decimal credit)
    {
        if (debit < 0 || credit < 0 || (debit == 0) == (credit == 0) || decimal.Round(debit, 2) != debit || decimal.Round(credit, 2) != credit)
            throw new DomainException("INVALID_LEDGER_ENTRY", "Entry requires exactly one positive debit or credit with two decimals.");
        Debit = debit; Credit = credit;
    }
    public decimal CustomerBalanceEffect => Debit - Credit;
    public LedgerAmount Reverse() => new(Credit, Debit);
}
