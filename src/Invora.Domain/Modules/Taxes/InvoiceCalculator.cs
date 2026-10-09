using Invora.Domain.Common;
namespace Invora.Domain.Modules.Taxes;

public sealed record InvoiceLineInput(decimal Quantity, decimal UnitPrice, decimal Discount, decimal GstRate, bool TaxInclusive);
public sealed record InvoiceLineTotal(decimal TaxableValue, decimal Cgst, decimal Sgst, decimal Igst, decimal Total);
public sealed record InvoiceTotal(IReadOnlyList<InvoiceLineTotal> Lines, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal Igst, decimal RoundOff, decimal Total);
public static class InvoiceCalculator
{
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    public static InvoiceTotal Calculate(IReadOnlyList<InvoiceLineInput> inputs, bool interstate, bool wholeRupeeRoundOff = false)
    {
        if (inputs.Count == 0) throw new DomainException("INVOICE_EMPTY", "At least one item is required.");
        var lines = inputs.Select(input => CalculateLine(input, interstate)).ToArray();
        var beforeRoundOff = lines.Sum(x => x.Total);
        var total = wholeRupeeRoundOff ? decimal.Round(beforeRoundOff, 0, MidpointRounding.AwayFromZero) : beforeRoundOff;
        return new(lines, lines.Sum(x => x.TaxableValue), lines.Sum(x => x.Cgst), lines.Sum(x => x.Sgst), lines.Sum(x => x.Igst), total - beforeRoundOff, total);
    }
    private static InvoiceLineTotal CalculateLine(InvoiceLineInput input, bool interstate)
    {
        if (input.Quantity <= 0 || input.UnitPrice < 0 || input.Discount < 0 || input.GstRate < 0 || input.GstRate > 100)
            throw new DomainException("INVALID_INVOICE_LINE", "Quantity, price, discount or rate is invalid.");
        var gross = Round(input.Quantity * input.UnitPrice);
        if (input.Discount > gross) throw new DomainException("DISCOUNT_EXCEEDS_PRICE", "Discount exceeds line value.");
        var net = Round(gross - input.Discount);
        var taxable = input.TaxInclusive ? Round(net / (1 + input.GstRate / 100)) : net;
        var tax = input.TaxInclusive ? net - taxable : Round(taxable * input.GstRate / 100);
        // Assign an odd paise deterministically so every line reconciles exactly.
        var cgst = interstate ? 0 : Round(tax / 2);
        var sgst = interstate ? 0 : tax - cgst;
        var igst = interstate ? tax : 0;
        return new(taxable, cgst, sgst, igst, taxable + tax);
    }
}
