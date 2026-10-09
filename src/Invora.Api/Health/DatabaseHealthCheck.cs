using Invora.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Invora.Api.Health;

public sealed class DatabaseHealthCheck(InvoraDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable."); }
        catch (Exception) { return HealthCheckResult.Unhealthy("Database unavailable."); }
    }
}
