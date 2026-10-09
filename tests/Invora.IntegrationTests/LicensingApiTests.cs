using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Invora.Contracts.Identity;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Licensing;
using Invora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;
namespace Invora.IntegrationTests;

public sealed class LicensingApiTests:IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres=new PostgreSqlBuilder("postgres:18.6").Build();
    private readonly RSA signer=RSA.Create(3072);private WebApplicationFactory<Program> factory=null!;private HttpClient client=null!;private Guid business,branch;
    private readonly LicenseClock clock=new();private const string Password="license-test-password-12345";
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{builder.UseEnvironment("Development");builder.UseSetting("ConnectionStrings:Invora",postgres.GetConnectionString());builder.UseSetting("Auth:SigningKey","licensing-integration-only-signing-12345678901234567890");builder.UseSetting("Auth:BootstrapKey","licensing-integration-bootstrap-1234567890");builder.UseSetting("Auth:BrowserOrigin","http://localhost");builder.UseSetting("Licensing:Required","true");builder.UseSetting("Licensing:PublicKey",Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()));builder.ConfigureServices(services=>{services.RemoveAll<TimeProvider>();services.AddSingleton<TimeProvider>(clock);});});
        client=factory.CreateClient(new(){HandleCookies=false});await using(var scope=factory.Services.CreateAsyncScope())await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Database.MigrateAsync();
        var setup=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/register"){Content=JsonContent.Create(new SetupRequest("Licence fixture","MAIN","Main","owner","Owner",Password))};setup.Headers.Add("X-Invora-Bootstrap","licensing-integration-bootstrap-1234567890");var response=await client.SendAsync(setup);response.EnsureSuccessStatusCode();var session=(await response.Content.ReadFromJsonAsync<SessionView>())!;branch=session.User.BranchIds[0];client.DefaultRequestHeaders.Authorization=new("Bearer",session.AccessToken);
        business=(await client.GetFromJsonAsync<JsonElement>("/api/v1/license")).GetProperty("businessId").GetGuid();
    }
    public async Task DisposeAsync(){client.Dispose();await factory.DisposeAsync();signer.Dispose();await postgres.DisposeAsync();}
    private string Key(Guid? shop=null,DateOnly? expires=null)=>ShopLicenseCodec.Sign(new(1,Guid.NewGuid(),shop??business,"Fixture","Retail",new(2026,10,1),expires??new(2026,10,7),7),signer);
    private Task<HttpResponseMessage> Post(string path,object body,string? key=null){var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1"+path){Content=JsonContent.Create(body)};request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return client.SendAsync(request);}
    [Fact]public async Task SetupAndReadingWorkBeforeActivationButPostingRequiresCorrectSignedShopKey()
    {
        Assert.Equal("AwaitingActivation",(await client.GetFromJsonAsync<JsonElement>("/api/v1/license")).GetProperty("state").GetString());Assert.Equal(HttpStatusCode.PaymentRequired,(await Post("/brands",new BrandRequest("Blocked"))).StatusCode);
        (await client.GetAsync("/api/v1/brands")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post("/license/activate",new{key=Key(Guid.NewGuid())})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/license/activate",new{key="INVORA1.changed.invalid"})).StatusCode);
        (await Post("/license/activate",new{key=Key()})).EnsureSuccessStatusCode();(await Post("/brands",new BrandRequest("Allowed"))).EnsureSuccessStatusCode();
    }
    [Fact]public async Task GraceAllowsPostingThenExpiryKeepsReportsExportsLoginAndRenewalAvailable()
    {
        var token=Key();var retryKey=Guid.NewGuid().ToString();(await Post("/license/activate",new{key=token},retryKey)).EnsureSuccessStatusCode();clock.Now=new(2026,10,14,12,0,0,TimeSpan.Zero);
        Assert.Equal("Grace",(await client.GetFromJsonAsync<JsonElement>("/api/v1/license")).GetProperty("state").GetString());(await Post("/brands",new BrandRequest("Grace"))).EnsureSuccessStatusCode();clock.Now=new(2026,10,15,12,0,0,TimeSpan.Zero);
        var replay=await Post("/license/activate",new{key=token},retryKey);replay.EnsureSuccessStatusCode();Assert.Equal("Expired",(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.PaymentRequired,(await Post("/brands",new BrandRequest("Expired"))).StatusCode);(await client.GetAsync($"/api/v1/exports/inventory?branchId={branch}")).EnsureSuccessStatusCode();(await client.GetAsync($"/api/v1/sales?branchId={branch}")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("owner",Password))).EnsureSuccessStatusCode();(await Post("/license/activate",new{key=Key(expires:new(2027,10,7))})).EnsureSuccessStatusCode();Assert.Equal("Active",(await client.GetFromJsonAsync<JsonElement>("/api/v1/license")).GetProperty("state").GetString());
    }
    [Fact]public async Task OnlyOwnerCanRenewAndActivationHistoryIsImmutableAndReplayable()
    {
        var token=Key(expires:new(2027,10,7));var idempotency=Guid.NewGuid().ToString();(await Post("/license/activate",new{key=token},idempotency)).EnsureSuccessStatusCode();(await Post("/license/activate",new{key=token},idempotency)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<InvoraDbContext>();Assert.Equal(1,await db.Set<BusinessLicense>().CountAsync());await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM \"BusinessLicense\""));
        Assert.Equal(HttpStatusCode.Conflict,(await Post("/license/activate",new{key=Key()})).StatusCode);
        (await client.PostAsJsonAsync("/api/v1/users",new StaffRequest("manager","Manager",Password,["business.settings"],[branch]))).EnsureSuccessStatusCode();var login=await client.PostAsJsonAsync("/api/v1/auth/login",new LoginRequest("manager",Password));login.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post("/license/activate",new{key=token})).StatusCode);(await client.GetAsync("/api/v1/license")).EnsureSuccessStatusCode();
    }
    private sealed class LicenseClock:TimeProvider{public DateTimeOffset Now=new(2026,10,7,12,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
}
