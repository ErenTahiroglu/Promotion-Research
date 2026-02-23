namespace PromoScanner.Core;

public class ResultRow
{
    public string Store { get; set; } = "";
    public string SeedUrl { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal? Price { get; set; }
    public decimal? ListPrice { get; set; }
    public string Currency { get; set; } = "";
    public bool RequiresQuote { get; set; }
    public bool HasKDV { get; set; }
    public int MinOrderQty { get; set; } = 1;
    public string QuantityPriceListJson { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Error { get; set; } = "";

    public static ResultRow ErrorRow(string store, string seed, string url, string err) => new()
    {
        Store = store,
        SeedUrl = seed,
        Url = url,
        Timestamp = DateTimeOffset.Now,
        Error = err
    };
}

public class ProductFeatures
{
    public string OriginalName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Capacity { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Color { get; set; } = "";
    public List<string> Properties { get; set; } = new();
}