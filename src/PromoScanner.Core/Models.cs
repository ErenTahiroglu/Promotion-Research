namespace PromoScanner.Core;

/// <summary>
/// Bir ürünün tek bir siteden çekilen bilgilerini tutar. Immutable.
/// </summary>
public sealed record ResultRow
{
    public string Store { get; init; } = "";
    public string SeedUrl { get; init; } = "";
    public string Url { get; init; } = "";
    public string Category { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal? Price { get; init; }
    public decimal? ListPrice { get; init; }
    public string Currency { get; init; } = "";
    public bool RequiresQuote { get; init; }
    public bool HasKDV { get; init; }
    public int MinOrderQty { get; init; } = 1;
    public string QuantityPriceListJson { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public string Error { get; init; } = "";

    public static ResultRow ErrorRow(string store, string seed, string url, string err) => new()
    {
        Store = store,
        SeedUrl = seed,
        Url = url,
        Timestamp = DateTimeOffset.Now,
        Error = err
    };
}