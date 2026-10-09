using System.Text.RegularExpressions;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;

namespace Invora.Infrastructure.Modules.Retail;

public static class GstSupply
{
    // Domestic state master published by the government e-way bill portal.
    public static readonly IReadOnlyDictionary<string,string> States = new Dictionary<string,string>
    {
        ["01"]="Jammu and Kashmir",["02"]="Himachal Pradesh",["03"]="Punjab",["04"]="Chandigarh",
        ["05"]="Uttarakhand",["06"]="Haryana",["07"]="Delhi",["08"]="Rajasthan",["09"]="Uttar Pradesh",
        ["10"]="Bihar",["11"]="Sikkim",["12"]="Arunachal Pradesh",["13"]="Nagaland",["14"]="Manipur",
        ["15"]="Mizoram",["16"]="Tripura",["17"]="Meghalaya",["18"]="Assam",["19"]="West Bengal",
        ["20"]="Jharkhand",["21"]="Odisha",["22"]="Chhattisgarh",["23"]="Madhya Pradesh",["24"]="Gujarat",
        ["26"]="Dadra and Nagar Haveli and Daman and Diu",["27"]="Maharashtra",["29"]="Karnataka",
        ["30"]="Goa",["31"]="Lakshadweep",["32"]="Kerala",["33"]="Tamil Nadu",["34"]="Puducherry",
        ["35"]="Andaman and Nicobar Islands",["36"]="Telangana",["37"]="Andhra Pradesh",["38"]="Ladakh",["97"]="Other territory"
    };
    public static string Normalize(string? value,bool optional=false)
    {
        var text=(value??"").Trim();if(optional&&text.Length==0)return "";
        var code=Regex.IsMatch(text,"^[0-9]{1,2}$")?text.PadLeft(2,'0'):States.FirstOrDefault(x=>x.Value.Equals(text,StringComparison.OrdinalIgnoreCase)).Key;
        RetailOperations.Check(code is not null&&States.ContainsKey(code),"Choose a valid Indian GST state for this contact or place of supply.");return code!;
    }
    public static string ContactState(string? state,string? gstin)
    {
        var code=Normalize(state,true);var gst=(gstin??"").Trim().ToUpperInvariant();if(gst.Length==0)return code;
        RetailOperations.Check(Regex.IsMatch(gst,"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$"),"Customer / supplier GSTIN must have the 15-character GST format.");
        var prefix=Normalize(gst[..2]);RetailOperations.Check(code.Length==0||prefix==code,"GSTIN state differs from the contact state. Choose the GSTIN's state or correct the GSTIN.");return prefix;
    }
    public static (string State,bool Interstate) Resolve(SaleRequest request,Party customer,SettingsRequest seller)
    {
        var sellerState=Normalize(seller.StateCode);var customerState=ContactState(customer.StateCode,customer.Gstin);
        var supply=string.IsNullOrWhiteSpace(request.SupplyStateCode)?(customerState.Length>0?customerState:sellerState):Normalize(request.SupplyStateCode);
        return(supply,supply!=sellerState);
    }
    public static string Label(string? code)=>States.TryGetValue(code??"",out var name)?name+" ("+code+")":code??"";
}
