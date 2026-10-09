namespace Invora.Contracts.Retail;

public static class WarrantyPolicy
{
    public const string Terms = "Warranty, where applicable, is covered by the manufacturer / authorized service partner, not the shop. Customers must visit the authorized service centre for service. Used phones are sold without warranty.";
    public static string ForDevice(string condition, int months) => condition != "New"
        ? condition + " device: No warranty"
        : months > 0 ? "Service partner warranty: " + months + " months" : "No warranty";
}
