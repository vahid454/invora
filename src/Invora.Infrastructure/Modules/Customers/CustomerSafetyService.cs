using System.Text.Json;
using Invora.Domain.Modules.Identity;
using Invora.Domain.Modules.Retail;
using Invora.Infrastructure.Modules.Retail;
using Invora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Invora.Infrastructure.Modules.Customers;

public sealed record CustomerSafetyRequest(Guid BranchId, string Status, string Reason);
public sealed record CustomerSafetyView(string Status, string Reason, DateTimeOffset? ChangedAtUtc);

// The immutable audit event is the source of truth. Clearing a flag appends another event;
// it never removes the earlier decision or changes the customer's financial history.
public sealed class CustomerSafetyService(RetailOperations r)
{
    private const string Prefix = "CUSTOMER_SAFETY_";
    public static async Task<string> StatusAsync(InvoraDbContext db, Guid customer, CancellationToken ct)
    {
        var action = await db.AccessAudits.AsNoTracking()
            .Where(x => x.ResourceId == customer && (x.Action == Prefix + "Blocked" || x.Action == Prefix + "FraudConcern" || x.Action == Prefix + "Clear"))
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => x.Action).FirstOrDefaultAsync(ct);
        return action is null ? "Clear" : action[Prefix.Length..];
    }
    public static async Task<Dictionary<Guid, string>> StatusesAsync(InvoraDbContext db, IEnumerable<Guid> customers, CancellationToken ct)
    {
        var ids = customers.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var events = await db.AccessAudits.AsNoTracking()
            .Where(x => ids.Contains(x.ResourceId) && (x.Action == Prefix + "Blocked" || x.Action == Prefix + "FraudConcern" || x.Action == Prefix + "Clear"))
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new { x.ResourceId, x.Action }).ToArrayAsync(ct);
        return events.GroupBy(x => x.ResourceId).ToDictionary(g => g.Key, g => g.First().Action[Prefix.Length..]);
    }
    public static async Task EnsureCanDealAsync(RetailOperations r, Guid customer, CancellationToken ct)
    {
        RetailOperations.Check(await StatusAsync(r.Db, customer, ct) == "Clear", "This customer is internally flagged. New sales and deals are blocked. Review the customer safety record with an owner.", "CUSTOMER_SAFETY_BLOCKED");
    }
    public static async Task EnsureCanContactAsync(RetailOperations r, Guid customer, CancellationToken ct)
    {
        RetailOperations.Check(await StatusAsync(r.Db, customer, ct) == "Clear", "This customer is internally flagged: do not call, message or send a payment reminder. Existing balances remain available for review.", "CUSTOMER_CONTACT_BLOCKED");
    }
    public async Task<CustomerSafetyView> GetAsync(Guid customer, Guid branch, CancellationToken ct)
    {
        await r.BranchAsync(branch, "customers.view", ct);
        await r.PartyAsync(customer, PartyKind.Customer, ct);
        var entry = await r.Db.AccessAudits.AsNoTracking()
            .Where(x => x.ResourceId == customer && (x.Action == Prefix + "Blocked" || x.Action == Prefix + "FraudConcern" || x.Action == Prefix + "Clear"))
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (entry is null) return new("Clear", "", null);
        string reason;
        try { reason = JsonDocument.Parse(entry.DetailsJson).RootElement.GetProperty("reason").GetString() ?? ""; }
        catch (JsonException) { reason = ""; }
        return new(entry.Action[Prefix.Length..], reason, entry.CreatedAtUtc);
    }
    public Task<CustomerSafetyView> SetAsync(Guid customer, CustomerSafetyRequest request, string key, CancellationToken ct) =>
        r.ExecuteAsync("customer-safety", key, new { customer, request }, async () =>
        {
            await r.BranchAsync(request.BranchId, "customers.manage", ct);
            await r.PermissionAsync("business.settings", ct);
            await r.PartyAsync(customer, PartyKind.Customer, ct);
            RetailOperations.Check(request.Status is "Clear" or "Blocked" or "FraudConcern", "Choose a valid internal customer status.");
            RetailOperations.Text(request.Reason, "Reason for this change", 500);
            var previous = await StatusAsync(r.Db, customer, ct);
            RetailOperations.Check(previous != request.Status, "The customer already has this status.");
            r.Audit(Prefix + request.Status, customer, new { previous, reason = request.Reason.Trim() });
            return new CustomerSafetyView(request.Status, request.Reason.Trim(), DateTimeOffset.UtcNow);
        }, ct);
}
