using Invora.Domain.Modules.Taxes;
using Invora.Domain.Modules.Invoices;
using Xunit;
namespace Invora.UnitTests;
public sealed class RetailCalculationTests
{
    [Theory]
    [InlineData(0.01,18,1)] [InlineData(0.02,18,10)] [InlineData(118,18,0)] [InlineData(129,18,11)]
    public void InclusiveTaxNeverLosesPaise(decimal price,decimal gst,decimal cess){var t=RetailTaxCalculator.Calculate(1,price,0,gst,cess,true,false);Assert.Equal(price,t.Total);Assert.Equal(price,t.Taxable+t.Cgst+t.Sgst+t.Igst+t.Cess);Assert.True(t.Cgst>=0 && t.Sgst>=0 && t.Cess>=0);}
    [Fact]public void InterstateTaxDoesNotCollectStateTax(){var t=RetailTaxCalculator.Calculate(2,100,0,18,1,false,true);Assert.Equal(200,t.Taxable);Assert.Equal(36,t.Igst);Assert.Equal(2,t.Cess);Assert.Equal(238,t.Total);Assert.Equal(0,t.Cgst+t.Sgst);}
    [Theory]
    [InlineData(222322,"INR Two Lakh Twenty Two Thousand Three Hundred Twenty Two Only")]
    [InlineData(33913.52,"INR Thirty Three Thousand Nine Hundred Thirteen and Fifty Two Paise Only")]
    [InlineData(0.01,"INR Zero and One Paise Only")]
    public void InvoiceWordsFollowIndianNumbering(decimal value,string expected)=>Assert.Equal(expected,IndianAmountWords.Format(value));
}
