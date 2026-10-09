using Invora.Infrastructure.Persistence;
using Invora.Domain.Modules.Businesses;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
namespace Invora.IntegrationTests;

public sealed class PersistenceTests
{
    [Fact]
    public void FinancialRelationshipsDoNotCascadeDelete()
    {
        using var db = new InvoraDbContext(new DbContextOptionsBuilder<InvoraDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        Assert.All(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }
    [Fact]
    public async Task PostgreSqlReadinessUsesRealDatabase()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18.6").Build();
        await postgres.StartAsync();
        await using var db = new InvoraDbContext(new DbContextOptionsBuilder<InvoraDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        Assert.True(await db.Database.CanConnectAsync());
        await db.Database.EnsureCreatedAsync();
        var business = BusinessProfile.Create("Smart Plaza");
        db.Businesses.Add(business);
        var branch = Branch.Create(business.Id, "SAR", "Sarangpur Main");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Branches.Add(Branch.Create(business.Id, "IND", "Indore"));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Branches.CountAsync());
        db.Businesses.Add(BusinessProfile.Create("Forbidden second business"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Branches.Add(Branch.Create(business.Id, "SAR", "Duplicate code"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Businesses.Remove(await db.Businesses.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
