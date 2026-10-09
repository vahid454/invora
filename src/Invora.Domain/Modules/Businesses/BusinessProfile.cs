using Invora.Domain.Common;
namespace Invora.Domain.Modules.Businesses;

public sealed class BusinessProfile : Entity
{
    private BusinessProfile() { }
    public int SingletonKey { get; private set; } = 1;
    public string TradeName { get; private set; } = "";
    public string TimeZone { get; private set; } = "Asia/Kolkata";
    public string Currency { get; private set; } = "INR";
    public int FinancialYearStartMonth { get; private set; } = 4;
    public void Update(string tradeName, string timeZone, int financialYearStartMonth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradeName);
        if (tradeName.Trim().Length > 200 || financialYearStartMonth is < 1 or > 12)
            throw new DomainException("VALIDATION_FAILED", "Business name or financial year month is invalid.");
        try { TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new DomainException("VALIDATION_FAILED", "Unknown time zone."); }
        TradeName = tradeName.Trim(); TimeZone = timeZone; FinancialYearStartMonth = financialYearStartMonth; Version++;
    }
    public static BusinessProfile Create(string tradeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradeName);
        return new() { TradeName = tradeName.Trim() };
    }
}
public sealed class Branch : Entity
{
    private Branch() { }
    public Guid BusinessProfileId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public static Branch Create(Guid businessId, string code, string name)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new() { BusinessProfileId = businessId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim() };
    }
}
