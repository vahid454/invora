using Invora.Domain.Common;
using Invora.Infrastructure.Modules.Licensing;
namespace Invora.Api.Middleware;
public sealed class ShopLicenseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,LicenseService license)
    {
        var path=context.Request.Path;
        if(context.User.Identity?.IsAuthenticated==true&&path.StartsWithSegments("/api/v1")&&
           !path.StartsWithSegments("/api/v1/auth")&&!path.StartsWithSegments("/api/v1/license")&&
           context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            var status=await license.StatusAsync(context.RequestAborted);
            if(!status.CanWrite)throw new DomainException("LICENSE_REQUIRED","Activate or renew this shop's licence before recording new transactions. Existing records, reports and exports remain available at /license.");
        }
        await next(context);
    }
}
