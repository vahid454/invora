using System.Text.RegularExpressions;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Modules.Businesses;
public sealed partial class SettingsService(RetailOperations r)
{
    public async Task<SettingsRequest> GetAsync(CancellationToken ct){await r.PermissionAsync("business.settings",ct);return await r.SettingsAsync(ct);}
    public Task<Guid> SaveAsync(SettingsRequest request,string key,CancellationToken ct)=>r.ExecuteAsync("settings-save",key,request,async()=>{
        await r.PermissionAsync("business.settings",ct);request=request with{WarrantyTerms=WarrantyPolicy.Terms,StateCode=GstSupply.Normalize(request.StateCode),Gstin=(request.Gstin??"").Trim().ToUpperInvariant()};RetailOperations.Check(request.StateCode is not null && StatePattern().IsMatch(request.StateCode),"State code must contain two digits.");
        RetailOperations.Check(!request.CompositionDealer || request.GstRegistered,"Composition dealer must be GST registered.");
        if(request.GstRegistered){RetailOperations.Text(request.LegalName,"Legal name",200);RetailOperations.Text(request.Address,"Seller address");RetailOperations.Check(request.Gstin is not null && GstPattern().IsMatch(request.Gstin) && request.Gstin.StartsWith(request.StateCode!,StringComparison.Ordinal),"GSTIN format or state prefix is invalid.");}
        var current=await r.Db.Set<BusinessSettings>().SingleOrDefaultAsync(ct);var before=current?.Json??"{}";if(current is null){current=new();r.Db.Add(current);}current.Json=RetailOperations.Json(request);r.Audit("BUSINESS_SETTINGS_UPDATED",current.Id,new{Before=new{previous=RetailOperations.Read<SettingsRequest>(before).LegalName,previousState=RetailOperations.Read<SettingsRequest>(before).StateCode},After=new{request.LegalName,request.Address,request.StateCode,request.GstRegistered,request.CompositionDealer,request.Declaration}});return current.Id;
    },ct);
    [GeneratedRegex("^[0-9]{2}$")] private static partial Regex StatePattern();
    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")] private static partial Regex GstPattern();
}
