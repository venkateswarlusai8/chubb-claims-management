namespace Claims.Api.Domain;

public record MarketInfo(string Code, string Name, string Currency);

/// <summary>
/// The six APAC markets. The brief does not name them, so this list is an assumption.
/// Kept as static reference data because it changes rarely and drives validation and currency.
/// </summary>
public static class Markets
{
    public static readonly IReadOnlyList<MarketInfo> All =
    [
        new("SG", "Singapore", "SGD"),
        new("AU", "Australia", "AUD"),
        new("HK", "Hong Kong", "HKD"),
        new("MY", "Malaysia", "MYR"),
        new("TH", "Thailand", "THB"),
        new("NZ", "New Zealand", "NZD")
    ];

    public static MarketInfo? Find(string? code) =>
        All.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    public static string NameOf(string code) => Find(code)?.Name ?? code;
}
