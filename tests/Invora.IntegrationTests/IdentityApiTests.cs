using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Invora.Contracts.Identity;
using Invora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
namespace Invora.IntegrationTests;

public sealed class IdentityApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.6").Build();
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    private const string Bootstrap = "integration-only-bootstrap-key-123456789";
    private const string Password = "integration-password-12345";
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Invora", postgres.GetConnectionString());
            builder.UseSetting("Auth:SigningKey", "integration-only-signing-key-12345678901234567890");
            builder.UseSetting("Auth:BootstrapKey", Bootstrap);
            builder.UseSetting("Auth:BrowserOrigin", "http://localhost");
        });
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InvoraDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await postgres.DisposeAsync(); }
    private async Task<HttpResponseMessage> SetupResponse()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register") { Content = JsonContent.Create(new SetupRequest("Smart Plaza", "SAR", "Sarangpur", "owner", "Owner", Password)) };
        req.Headers.Add("X-Invora-Bootstrap", Bootstrap); return await client.SendAsync(req);
    }
    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    private async Task<(SessionView View, string Cookie)> Setup()
    {
        var response = await SetupResponse(); response.EnsureSuccessStatusCode(); return ((await response.Content.ReadFromJsonAsync<SessionView>())!, Cookie(response));
    }
    private void Bearer(string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private async Task<HttpResponseMessage> Refresh(string cookie, string origin = "http://localhost", bool csrf = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh"); req.Headers.Add("Cookie", cookie); req.Headers.Add("Origin", origin); if (csrf) req.Headers.Add("X-Invora-CSRF", "1"); return await client.SendAsync(req);
    }
    [Fact]
    public async Task SetupRequiresBootstrapAndConcurrentRegistrationCreatesOneBusiness()
    {
        var denied = await client.PostAsJsonAsync("/api/v1/auth/register", new SetupRequest("Smart Plaza", "SAR", "Sarangpur", "owner", "Owner", Password)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var responses = await Task.WhenAll(SetupResponse(), SetupResponse());
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
        Assert.Equal(1, await db.Businesses.CountAsync()); Assert.Equal(1, await db.StaffUsers.CountAsync());
        var hash = (await db.StaffUsers.SingleAsync()).PasswordHash; Assert.NotEqual(Password, hash); Assert.NotEmpty(hash);
        Assert.Equal(1, await db.AccessAudits.CountAsync(x => x.Action == "BUSINESS_SETUP"));
    }
    [Fact]
    public async Task RotationReplayRevokesBothAccessAndReplacementRefresh()
    {
        var owner = await Setup(); var rotated = await Refresh(owner.Cookie); rotated.EnsureSuccessStatusCode();
        var replacement = (await rotated.Content.ReadFromJsonAsync<SessionView>())!;
        Assert.NotEqual(owner.Cookie, Cookie(rotated));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(owner.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(Cookie(rotated))).StatusCode);
        Bearer(replacement.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [Fact]
    public async Task StaffCannotAccessOtherBranchesEscalateOrKeepRevokedSession()
    {
        var owner = await Setup(); Bearer(owner.View.AccessToken); var initial = owner.View.User.BranchIds.Single();
        var branchResponse = await client.PostAsJsonAsync("/api/v1/branches", new BranchRequest("IND", "Indore")); branchResponse.EnsureSuccessStatusCode(); var second = (await branchResponse.Content.ReadFromJsonAsync<BranchView>())!;
        var staffResponse = await client.PostAsJsonAsync("/api/v1/users", new StaffRequest("cashier", "Cashier", Password, ["sales.create"], [initial])); staffResponse.EnsureSuccessStatusCode(); var staff = (await staffResponse.Content.ReadFromJsonAsync<UserView>())!;
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("cashier", Password)); login.EnsureSuccessStatusCode(); var session = (await login.Content.ReadFromJsonAsync<SessionView>())!;
        Bearer(session.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/branches/{second.Id}")).StatusCode);
        var visible = (await client.GetFromJsonAsync<BranchView[]>("/api/v1/branches"))!; Assert.Single(visible); Assert.Equal(initial, visible[0].Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/branches", new BranchRequest("BAD", "Forbidden"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/users/{staff.Id}/access", new AccessRequest(["users.manage"], [initial], true))).StatusCode);
        Bearer(owner.View.AccessToken);
        var change = await client.PutAsJsonAsync($"/api/v1/users/{staff.Id}/access", new AccessRequest(["sales.create"], [initial], false)); change.EnsureSuccessStatusCode();
        Bearer(session.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("cashier", Password))).StatusCode);
    }
    [Fact]
    public async Task BrowserCsrfValidationOwnerProtectionAndLogoutWork()
    {
        var owner = await Setup();
        Assert.Equal(HttpStatusCode.Forbidden, (await Refresh(owner.Cookie, "http://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Refresh(owner.Cookie, csrf: false)).StatusCode);
        Bearer(owner.View.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/users/{owner.View.User.Id}/access", new AccessRequest([], owner.View.User.BranchIds, false))).StatusCode);
        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout"); logout.Headers.Add("Cookie", owner.Cookie); logout.Headers.Add("Origin", "http://localhost"); logout.Headers.Add("X-Invora-CSRF", "1");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [Fact]
    public async Task DelegatedManagerCannotGrantHigherPermissionsOrAnotherBranch()
    {
        var owner = await Setup(); Bearer(owner.View.AccessToken);
        var secondResponse = await client.PostAsJsonAsync("/api/v1/branches", new BranchRequest("IND", "Indore")); secondResponse.EnsureSuccessStatusCode();
        var second = (await secondResponse.Content.ReadFromJsonAsync<BranchView>())!;
        var managerResponse = await client.PostAsJsonAsync("/api/v1/users", new StaffRequest("manager", "Manager", Password, ["users.manage"], owner.View.User.BranchIds)); managerResponse.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("manager", Password)); login.EnsureSuccessStatusCode();
        Bearer((await login.Content.ReadFromJsonAsync<SessionView>())!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/users", new StaffRequest("escalation", "Escalation", Password, ["profit.view"], owner.View.User.BranchIds))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/users", new StaffRequest("otherbranch", "Other Branch", Password, [], [second.Id]))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>(); Assert.Equal(2, await db.StaffUsers.CountAsync());
    }
    [Fact]
    public async Task BusinessUpdateIsValidatedAuditedAndPermissionProtected()
    {
        var owner = await Setup(); Bearer(owner.View.AccessToken);
        var updated = await client.PutAsJsonAsync("/api/v1/business", new ProfileRequest("Smart Plaza Retail", "Asia/Kolkata", 4)); updated.EnsureSuccessStatusCode();
        var profile = (await updated.Content.ReadFromJsonAsync<BusinessView>())!; Assert.Equal("Smart Plaza Retail", profile.TradeName);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/business", new ProfileRequest("Test", "Invalid/Zone", 4))).StatusCode);
        Assert.Equal("Smart Plaza Retail", (await client.GetFromJsonAsync<BusinessView>("/api/v1/business"))!.TradeName);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>(); Assert.Equal(1, await db.AccessAudits.CountAsync(x => x.Action == "BUSINESS_UPDATED"));
        var updateAudit = await db.AccessAudits.SingleAsync(x => x.Action == "BUSINESS_UPDATED");
        Assert.Contains("Smart Plaza Retail", updateAudit.DetailsJson);
        Assert.Contains("Before", updateAudit.DetailsJson);
        Assert.DoesNotContain(Password, updateAudit.DetailsJson);
        var audit = await db.AccessAudits.FirstAsync(); db.AccessAudits.Remove(audit); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task PasswordChangeRevokesAllSessionsAndOldPassword()
    {
        var owner = await Setup(); Bearer(owner.View.AccessToken);
        var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(Password, "replacement-password-12345")); Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(owner.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner", Password))).StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner", "replacement-password-12345")); login.EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task MalformedRequestsAndPasswordFailuresNeverExposeSecrets()
    {
        var owner = await Setup(); Bearer(owner.View.AccessToken);
        var bad = await client.PostAsJsonAsync("/api/v1/users", new { Login = "test", DisplayName = "Test", Password, Permissions = (string[]?)null, BranchIds = owner.View.User.BranchIds }); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner", "wrong-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner", Password))).StatusCode);
        var me = await client.GetAsync("/api/v1/auth/me"); me.EnsureSuccessStatusCode(); Assert.DoesNotContain("passwordHash", await me.Content.ReadAsStringAsync());
    }
}
