using System.Security.Cryptography;
using Invora.Domain.Common;
using Invora.Domain.Modules.Licensing;
using Xunit;
namespace Invora.UnitTests;
public sealed class ShopLicenseTests
{
    private static ShopLicenseClaims Claims()=>new(1,Guid.NewGuid(),Guid.NewGuid(),"Fixture shop","Retail",new(2026,10,1),new(2026,10,7),7);
    [Fact]public void SignedClaimsRoundTripAndCannotBeVerifiedWithAnotherVendorKey(){using var signer=RSA.Create(3072);using var other=RSA.Create(3072);var claims=Claims();var token=ShopLicenseCodec.Sign(claims,signer);Assert.Equal(claims,ShopLicenseCodec.Verify(token,Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));Assert.Throws<DomainException>(()=>ShopLicenseCodec.Verify(token,Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())));}
    [Fact]public void ChangedPayloadAndMalformedKeysAreRejected(){using var signer=RSA.Create(3072);var token=ShopLicenseCodec.Sign(Claims(),signer);var pieces=token.Split('.');pieces[1]=(pieces[1][0]=='A'?"B":"A")+pieces[1][1..];var key=Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());Assert.Throws<DomainException>(()=>ShopLicenseCodec.Verify(string.Join('.',pieces),key));Assert.Throws<DomainException>(()=>ShopLicenseCodec.Verify("not-a-license",key));}
    [Theory][InlineData(2026,9,30,"NotStarted")][InlineData(2026,10,7,"Active")][InlineData(2026,10,8,"Grace")][InlineData(2026,10,14,"Grace")][InlineData(2026,10,15,"Expired")]
    public void ValidityAndGraceDatesHaveExplicitBoundaries(int year,int month,int day,string state)=>Assert.Equal(state,ShopLicenseCodec.State(Claims(),new(year,month,day)));
}
