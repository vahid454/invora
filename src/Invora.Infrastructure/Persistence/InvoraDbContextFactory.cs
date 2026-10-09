using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Invora.Infrastructure.Persistence;

public sealed class InvoraDbContextFactory : IDesignTimeDbContextFactory<InvoraDbContext>
{
    public InvoraDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Invora") ?? "Host=localhost;Database=invora_design_only";
        return new(new DbContextOptionsBuilder<InvoraDbContext>().UseNpgsql(connection).Options);
    }
}
