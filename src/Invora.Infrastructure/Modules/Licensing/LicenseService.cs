using Invora.Domain.Common;
using Invora.Domain.Modules.Licensing;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Invora.Infrastructure.Modules.Licensing;

public sealed record LicenseStatus(bool Required,string State,Guid? BusinessId,string BusinessName,string Plan,DateOnly? ExpiresOn,DateOnly? GraceUntil,bool CanWrite);
public sealed record ActivateLicenseRequest(string Key);
public sealed class LicenseService(RetailOperations r,IConfiguration configuration,TimeProvider time)
{
    private bool Required=>configuration.GetValue<bool>("Licensing:Required");
    private string PublicKey=>configuration["Licensing:PublicKey"]??"";
    public async Task<LicenseStatus> StatusAsync(CancellationToken ct)
    {
        var business=await r.Db.Businesses.AsNoTracking().SingleOrDefaultAsync(ct);
        if(!Required)return new(false,"Disabled",business?.Id,business?.TradeName??"","",null,null,true);
        var latest=await r.Db.Set<BusinessLicense>().AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        if(string.IsNullOrWhiteSpace(PublicKey))return new(true,"Unconfigured",business?.Id,business?.TradeName??"","",null,null,false);
        if(business is null||latest is null)return new(true,"AwaitingActivation",business?.Id,business?.TradeName??"","",null,null,false);
        try
        {
            var claims=ShopLicenseCodec.Verify(latest.Token,PublicKey);
            if(claims.BusinessId!=business.Id)return new(true,"Invalid",business.Id,business.TradeName,"",null,null,false);
            var state=ShopLicenseCodec.State(claims,DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
            return new(true,state,business.Id,business.TradeName,claims.Plan,claims.ExpiresOn,claims.ExpiresOn.AddDays(claims.GraceDays),state is "Active" or "Grace");
        }
        catch(DomainException){return new(true,"Invalid",business.Id,business.TradeName,"",null,null,false);}
    }
    public Task<LicenseStatus> ActivateAsync(ActivateLicenseRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("license-activate",key,request,async()=>
    {
        RetailOperations.Check(await r.Db.StaffUsers.AnyAsync(x=>x.Id==r.Actor&&x.IsOwner&&x.IsActive,ct),"Only the business owner can activate or renew a shop licence.","OWNER_PROTECTED");
        RetailOperations.Check(!string.IsNullOrWhiteSpace(PublicKey),"The vendor's public verification key is not configured. Contact the vendor.");
        var claims=ShopLicenseCodec.Verify(request.Key,PublicKey);var business=await r.Db.Businesses.SingleAsync(ct);
        RetailOperations.Check(claims.BusinessId==business.Id,"This licence belongs to a different Shop ID.","INVALID_LICENSE");
        RetailOperations.Check(ShopLicenseCodec.State(claims,DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)) is "Active" or "Grace","This licence is not currently valid. Ask the vendor for a current renewal.","INVALID_LICENSE");
        var previous=await r.Db.Set<BusinessLicense>().OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        if(previous is not null){var old=ShopLicenseCodec.Verify(previous.Token,PublicKey);RetailOperations.Check(claims.ExpiresOn>=old.ExpiresOn,"This key would shorten the current licence. Use the latest renewal.","INVALID_LICENSE");if(previous.Token==request.Key.Trim())return await StatusAsync(ct);}
        r.Db.Add(new BusinessLicense{BusinessId=business.Id,ActorId=r.Actor,Token=request.Key.Trim()});r.Audit("SHOP_LICENSE_ACTIVATED",claims.LicenseId,new{claims.BusinessId,claims.Plan,claims.ExpiresOn});await r.Db.SaveChangesAsync(ct);return await StatusAsync(ct);
    },ct);
}
