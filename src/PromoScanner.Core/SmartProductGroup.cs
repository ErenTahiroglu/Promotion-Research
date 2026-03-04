namespace PromoScanner.Core;

/// <summary>
/// Birden fazla sitedeki benzer ürünleri temsil eder.
/// </summary>
public sealed class SmartProductGroup
{
    public string Category { get; set; } = "";
    public string Capacity { get; set; } = "";
    public string KeyFeatures { get; set; } = "";
    public int ProductCount { get; set; }
    public int SiteCount { get; set; }
    public decimal? MinPrice { get; set; }
    public string MinPriceStore { get; set; } = "";
    public string MinPriceUrl { get; set; } = "";
    public int MinPriceMinQty { get; set; } = 1;
    public decimal? MinPriceTotalCost { get; set; }
    public decimal? MaxPrice { get; set; }
    public string MaxPriceStore { get; set; } = "";
    public string MaxPriceUrl { get; set; } = "";
    public int MaxPriceMinQty { get; set; } = 1;
    public decimal? MaxPriceTotalCost { get; set; }
    public decimal? PriceDifference { get; set; }
    public decimal? AvgPrice { get; set; }
    public int MinOrderQty { get; set; } = 1;
    public string SiteCostBreakdown { get; set; } = "";
    public string AllProductNames { get; set; } = "";
    public string AllStores { get; set; } = "";
    public string AllUrls { get; set; } = "";
}