namespace Invora.Domain.Modules.Retail;

/// <summary>A stable lookup key, never an authorization token or a price snapshot.</summary>
public readonly record struct ItemLabelCode(string Kind, Guid Id)
{
    public string Value => $"INVORA:{(Kind == "unit" ? "U" : "P")}:{Id:N}".ToUpperInvariant();

    public static bool TryParse(string? value, out ItemLabelCode code)
    {
        code = default;
        var parts = value?.Trim().Split(':');
        if (parts is not { Length: 3 } || !parts[0].Equals("INVORA", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(parts[2], "N", out var id) || id == Guid.Empty) return false;
        var kind = parts[1].ToUpperInvariant() switch { "U" => "unit", "P" => "product", _ => null };
        if (kind is null) return false;
        code = new(kind, id);
        return true;
    }
}
