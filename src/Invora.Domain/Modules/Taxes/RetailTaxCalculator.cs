using Invora.Domain.Common;
namespace Invora.Domain.Modules.Taxes;
public sealed record RetailTaxTotal(decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Cess, decimal Total);
public static class RetailTaxCalculator
{
    public static RetailTaxTotal Calculate(int quantity, decimal price, decimal discount, decimal rate, decimal cessRate, bool inclusive, bool interstate)
    {
        if(quantity<=0 || price<0 || discount<0 || rate<0 || rate>100 || cessRate<0 || cessRate>100)throw new DomainException("VALIDATION_FAILED","Invalid item quantity, price or tax rate.");
        var gross=InvoiceCalculator.Round(quantity*price);if(discount>gross)throw new DomainException("VALIDATION_FAILED","Discount exceeds item value.");
        var net=InvoiceCalculator.Round(gross-discount);
        var taxable=inclusive?InvoiceCalculator.Round(net/(1+(rate+cessRate)/100)):net;
        var totalInclusiveTax=net-taxable;
        var cess=inclusive?(rate+cessRate==0?0:InvoiceCalculator.Round(totalInclusiveTax*cessRate/(rate+cessRate))):InvoiceCalculator.Round(taxable*cessRate/100);
        var gst=inclusive?totalInclusiveTax-cess:InvoiceCalculator.Round(taxable*rate/100);
        var cgst=interstate?0:InvoiceCalculator.Round(gst/2);
        return new(taxable,cgst,interstate?0:gst-cgst,interstate?gst:0,cess,taxable+gst+cess);
    }
}
