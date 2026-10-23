namespace Invora.Domain.Modules.Retail;

public static class FinanceProviderNames
{
    public static string? Canonical(string? provider) => (provider ?? "").Trim().ToUpperInvariant() switch
    {
        "BAJAJ" => "BAJAJ",
        "HDBS" => "HDBS",
        "TVS" => "TVS",
        "DMI" => "DMI",
        "HOMECREDIT" => "HomeCredit",
        "CHOLA" => "Chola",
        _ => null
    };
}
