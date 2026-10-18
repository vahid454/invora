using System.Net.Http.Json;
using System.Text.Json;
using Invora.Contracts.Retail;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Invora.IntegrationTests;

public sealed partial class RetailApiTests
{
    private async Task<HttpResponseMessage> Safety(string status, string reason, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customer}/safety")
        {
            Content = JsonContent.Create(new { branchId = branch, status, reason })
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task InternalSafetyFlagSuppressesRemindersAndNewDealsButPreservesReceivables()
    {
        var due = await Id("/lenden/existing-dues", OldDue(amount: 500));
        var pending = await Document("/sales", new SaleRequest(branch, customer, date, date.AddDays(7), false, [new(cable, 1, 200)], []));
        var key = Guid.NewGuid().ToString();
        var blocked = await Safety("FraudConcern", "Owner reviewed a threatening visit", key);
        Assert.True(blocked.IsSuccessStatusCode, await blocked.Content.ReadAsStringAsync());
        Assert.True((await Safety("FraudConcern", "Owner reviewed a threatening visit", key)).IsSuccessStatusCode);
        var ownerAuthorization = client.DefaultRequestHeaders.Authorization;
        (await client.PostAsJsonAsync("/api/v1/users", new Invora.Contracts.Identity.StaffRequest("safety-clerk", "Safety clerk", "safety-clerk-password-2026", ["customers.view", "customers.manage"], [branch]))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new Invora.Contracts.Identity.LoginRequest("safety-clerk", "safety-clerk-password-2026"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<Invora.Contracts.Identity.SessionView>())!.AccessToken);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await Safety("Clear", "Unapproved removal")).StatusCode);
        client.DefaultRequestHeaders.Authorization = ownerAuthorization;
        // A direct customer read and both money reports must disclose the internal status.
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}?branchId={branch}");
        Assert.Equal("FraudConcern", detail.GetProperty("party").GetProperty("safetyStatus").GetString());
        var register = await Collections();
        Assert.Equal("FraudConcern", register.GetProperty("items")[0].GetProperty("safetyStatus").GetString());
        Assert.Equal(500, register.GetProperty("totalOutstanding").Amount());
        var csv = await client.GetStringAsync($"/api/v1/lenden/collections/export?branchId={branch}");
        Assert.Contains("DO NOT CONTACT", csv);
        Assert.DoesNotContain("9000000002", csv);
        Assert.False((await client.GetAsync($"/api/v1/lenden/collections/{customer}/reminder?branchId={branch}")).IsSuccessStatusCode);
        Assert.False((await Post("/lenden/followups", new FollowUpRequest(branch, customer, "Reminder", "Do not contact"))).IsSuccessStatusCode);
        Assert.False((await Post("/lenden/followups", new FollowUpRequest(branch, customer, "Note", "No call", NextContactDate:date.AddDays(2)))).IsSuccessStatusCode);
        await Id("/lenden/followups", new FollowUpRequest(branch, customer, "Note", "Internal review only"));
        Assert.False((await Post("/lenden/entries", new LenDenRequest(branch, customer, "Out", 100, 1, date, "", "Another advance"))).IsSuccessStatusCode);
        Assert.False((await Post("/sales", new SaleRequest(branch, customer, date, date.AddDays(7), false, [new(cable, 1, 200)], []))).IsSuccessStatusCode);
        Assert.False((await Post($"/sales/{pending.Id}/complete", new { })).IsSuccessStatusCode);
        await Id("/payments", new PaymentRequest(branch, customer, 200, 1, date, "", "Received outstanding amount", [], ExistingDueId: due));
        Assert.Equal(300, (await Collections()).GetProperty("totalOutstanding").Amount());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InvoraDbContext>();
            Assert.Single(await db.AccessAudits.Where(x => x.ResourceId == customer && x.Action == "CUSTOMER_SAFETY_FraudConcern").ToArrayAsync());
        }
        Assert.True((await Safety("Clear", "Owner reviewed and cleared the flag")).IsSuccessStatusCode);
        Assert.Equal("Clear", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}/safety?branchId={branch}")).GetProperty("status").GetString());
        Assert.True((await client.GetAsync($"/api/v1/lenden/collections/{customer}/reminder?branchId={branch}")).IsSuccessStatusCode);
    }
}
