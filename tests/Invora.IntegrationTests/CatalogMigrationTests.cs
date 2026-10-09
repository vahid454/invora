using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
namespace Invora.IntegrationTests;
public sealed class CatalogMigrationTests
{
    [Fact]
    public async Task AdditiveCatalogMigrationPreservesExistingRatesAndExtendsRestrictedRuntimeRole()
    {
        await using var postgres=new PostgreSqlBuilder("postgres:18.6").Build();await postgres.StartAsync();
        await using var owner=new InvoraDbContext(new DbContextOptionsBuilder<InvoraDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await owner.GetService<IMigrator>().MigrateAsync("20261006100652_ReviewedOpeningInventoryImports");
        var tax=new TaxRate{Name="Owner configured 18%",Rate=18};owner.Add(tax);await owner.SaveChangesAsync();
        await owner.Database.ExecuteSqlRawAsync("CREATE ROLE invora_runtime LOGIN PASSWORD 'isolated-role-test-password'; GRANT USAGE ON SCHEMA public TO invora_runtime; GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA public TO invora_runtime;");
        await owner.Database.MigrateAsync();
        Assert.Equal(tax.Id,(await owner.Set<TaxRate>().SingleAsync(x=>x.Rate==18&&x.CessRate==0)).Id);
        var connection=new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()){Username="invora_runtime",Password="isolated-role-test-password"};
        await using var runtime=new InvoraDbContext(new DbContextOptionsBuilder<InvoraDbContext>().UseNpgsql(connection.ConnectionString).Options);
        Assert.True(await runtime.Set<ProductCategory>().AnyAsync(x=>x.Name=="Mobile"&&x.RequiresImei));runtime.Add(new ProductCategory{Name="Runtime category",NormalizedName="RUNTIME CATEGORY"});await runtime.SaveChangesAsync();
        Assert.True(await runtime.Set<Brand>().AnyAsync(x=>x.Name=="Apple"));runtime.Add(new Brand{Name="Runtime custom brand",NormalizedName="RUNTIME CUSTOM BRAND"});await runtime.SaveChangesAsync();
        var business=Invora.Domain.Modules.Businesses.BusinessProfile.Create("Migration fixture");var branch=Invora.Domain.Modules.Businesses.Branch.Create(business.Id,"MIG","Migration counter");var party=new Party{Kind=PartyKind.Customer,Name="Migration customer",Phone="9000000050",AlternatePhone="9000000051"};owner.AddRange(business,branch,party);await owner.SaveChangesAsync();
        runtime.Add(new PaymentFollowUp{BranchId=branch.Id,CustomerId=party.Id,ActorId=Guid.NewGuid(),Kind="Promise",Note="Restricted runtime promise",PromiseDate=new DateOnly(2026,10,8)});await runtime.SaveChangesAsync();Assert.Single(await runtime.Set<PaymentFollowUp>().ToArrayAsync());
        var model=new ProductModel{Name="Migration phone",Brand="Fixture",Category="Mobile",Hsn="85171300",TaxRateId=tax.Id};var variant=new ProductVariant{ProductModelId=model.Id,Sku="MIGRATION-PHONE",Serialized=true};var unit=new StockUnit{BranchId=branch.Id,ProductVariantId=variant.Id};owner.AddRange(model,variant,unit);await owner.SaveChangesAsync();
        runtime.Add(new DeviceCorrection{StockUnitId=unit.Id,BranchId=branch.Id,ActorId=Guid.NewGuid(),Reason="Restricted runtime correction"});await runtime.SaveChangesAsync();Assert.Single(await runtime.Set<DeviceCorrection>().ToArrayAsync());
        var correctionDelete=await Assert.ThrowsAsync<PostgresException>(()=>owner.Database.ExecuteSqlRawAsync("DELETE FROM \"DeviceCorrection\""));Assert.Equal("23514",correctionDelete.SqlState);
        Assert.Equal("9000000051",(await runtime.Set<Party>().SingleAsync()).AlternatePhone);
        var history=await Assert.ThrowsAsync<PostgresException>(()=>owner.Database.ExecuteSqlRawAsync("DELETE FROM \"PaymentFollowUp\""));Assert.Equal("23514",history.SqlState);
        Assert.True(await owner.Set<Brand>().AnyAsync(x=>x.NormalizedName=="RUNTIME CUSTOM BRAND"));
        var update=await Assert.ThrowsAsync<PostgresException>(()=>runtime.Database.ExecuteSqlRawAsync("UPDATE \"Brand\" SET \"Name\"='Changed'"));Assert.Equal("42501",update.SqlState);
        var deletion=await Assert.ThrowsAsync<PostgresException>(()=>runtime.Database.ExecuteSqlRawAsync("DELETE FROM \"Brand\""));Assert.Equal("42501",deletion.SqlState);
    }
}
