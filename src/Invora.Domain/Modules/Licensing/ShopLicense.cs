using System.Security.Cryptography;
using System.Text.Json;
using Invora.Domain.Common;
namespace Invora.Domain.Modules.Licensing;

public sealed record ShopLicenseClaims(int Version,Guid LicenseId,Guid BusinessId,string CustomerName,string Plan,DateOnly StartsOn,DateOnly ExpiresOn,int GraceDays);
public static class ShopLicenseCodec
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static string Sign(ShopLicenseClaims claims,RSA privateKey)
    {
        Validate(claims);var bytes=JsonSerializer.SerializeToUtf8Bytes(claims,Json);
        return "INVORA1."+Encode(bytes)+"."+Encode(privateKey.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pss));
    }
    public static ShopLicenseClaims Verify(string token,string publicKey)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(token)||token.Length>12000)throw new FormatException();
            var pieces=token.Trim().Split('.');if(pieces.Length!=3||pieces[0]!="INVORA1")throw new FormatException();
            var payload=Decode(pieces[1]);using var rsa=RSA.Create();var key=Convert.FromBase64String(publicKey);rsa.ImportSubjectPublicKeyInfo(key,out var read);if(read!=key.Length||rsa.KeySize<3072)throw new CryptographicException();
            if(!rsa.VerifyData(payload,Decode(pieces[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new CryptographicException();
            var claims=JsonSerializer.Deserialize<ShopLicenseClaims>(payload,Json)??throw new FormatException();Validate(claims);return claims;
        }
        catch(Exception error) when(error is FormatException or CryptographicException or JsonException or ArgumentException)
        {throw new DomainException("INVALID_LICENSE","Licence signature or format is invalid. Ask the vendor for the licence issued for this shop.");}
    }
    public static string State(ShopLicenseClaims claims,DateOnly today)
    {
        if(today<claims.StartsOn)return "NotStarted";
        if(today<=claims.ExpiresOn)return "Active";
        return today<=claims.ExpiresOn.AddDays(claims.GraceDays)?"Grace":"Expired";
    }
    private static void Validate(ShopLicenseClaims claims)
    {
        if(claims.Version!=1||claims.LicenseId==Guid.Empty||claims.BusinessId==Guid.Empty||string.IsNullOrWhiteSpace(claims.CustomerName)||claims.CustomerName.Length>200||claims.Plan!="Retail"||claims.ExpiresOn<claims.StartsOn||claims.GraceDays is <0 or >30||claims.ExpiresOn>new DateOnly(9999,11,30))throw new FormatException();
    }
    private static string Encode(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Decode(string value)=>Convert.FromBase64String(value.Replace('-','+').Replace('_','/').PadRight((value.Length+3)/4*4,'='));
}
public sealed class BusinessLicense : Entity
{
    public Guid BusinessId {get;set;}
    public Guid ActorId {get;set;}
    public string Token {get;set;}="";
}
