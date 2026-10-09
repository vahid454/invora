namespace Invora.Domain.Modules.Invoices;
public static class IndianAmountWords
{
    private static readonly string[] Small=["Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Nine","Ten","Eleven","Twelve","Thirteen","Fourteen","Fifteen","Sixteen","Seventeen","Eighteen","Nineteen"];
    private static readonly string[] Tens=["","","Twenty","Thirty","Forty","Fifty","Sixty","Seventy","Eighty","Ninety"];
    public static string Format(decimal value)
    {
        if(value<0 || value>99999999999999m)throw new ArgumentOutOfRangeException(nameof(value));
        var rounded=decimal.Round(value,2,MidpointRounding.AwayFromZero);var rupees=(long)rounded;var paise=(int)((rounded-rupees)*100);
        return "INR "+Words(rupees)+(paise>0?" and "+Words(paise)+" Paise":"")+" Only";
    }
    private static string Words(long n)
    {
        if(n<20)return Small[n];if(n<100)return Tens[n/10]+(n%10>0?" "+Words(n%10):"");
        if(n<1000)return Words(n/100)+" Hundred"+Tail(n%100);
        if(n<100000)return Words(n/1000)+" Thousand"+Tail(n%1000);
        if(n<10000000)return Words(n/100000)+" Lakh"+Tail(n%100000);
        return Words(n/10000000)+" Crore"+Tail(n%10000000);
    }
    private static string Tail(long n)=>n>0?" "+Words(n):"";
}
