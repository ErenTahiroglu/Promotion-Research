namespace PromoScanner.Core;

public class ResultRow
{
    public string Store { get; set; } = "";
    public string SeedUrl { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "";
    public bool RequiresQuote { get; set; }
    public bool HasKDV { get; set; }
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

public class SmartProductGroup
{
    public string Category { get; set; } = "";
    public string Capacity { get; set; } = "";
    public string KeyFeatures { get; set; } = "";
    public int ProductCount { get; set; }
    public int SiteCount { get; set; }
    public decimal? MinPrice { get; set; }
    public string MinPriceStore { get; set; } = "";
    public string MinPriceUrl { get; set; } = "";
    public decimal? MaxPrice { get; set; }
    public string MaxPriceStore { get; set; } = "";
    public decimal? PriceDifference { get; set; }
    public decimal? AvgPrice { get; set; }
    public string AllProductNames { get; set; } = "";
    public string AllStores { get; set; } = "";
    public List<ResultRow> Products { get; set; } = new();
}