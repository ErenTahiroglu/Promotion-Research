using CsvHelper.Configuration.Attributes;

namespace PromoScanner.Core;

/// <summary>
/// Birden fazla sitedeki benzer urunleri temsil eder.
/// </summary>
public class SmartProductGroup
{
    [Name("Category")]
    public string Category { get; set; } = "";

    [Name("Capacity")]
    public string Capacity { get; set; } = "";

    [Name("KeyFeatures")]
    public string KeyFeatures { get; set; } = "";

    [Name("ProductCount")]
    public int ProductCount { get; set; }

    [Name("SiteCount")]
    public int SiteCount { get; set; }

    [Name("MinPrice")]
    public decimal? MinPrice { get; set; }

    [Name("MinPriceStore")]
    public string MinPriceStore { get; set; } = "";

    [Name("MinPriceUrl")]
    public string MinPriceUrl { get; set; } = "";

    [Name("MinPriceMinQty")]
    public int MinPriceMinQty { get; set; } = 1;

    [Name("MinPriceTotalCost")]
    public decimal? MinPriceTotalCost { get; set; }

    [Name("MaxPrice")]
    public decimal? MaxPrice { get; set; }

    [Name("MaxPriceStore")]
    public string MaxPriceStore { get; set; } = "";

    [Name("MaxPriceUrl")]
    public string MaxPriceUrl { get; set; } = "";

    [Name("MaxPriceMinQty")]
    public int MaxPriceMinQty { get; set; } = 1;

    [Name("MaxPriceTotalCost")]
    public decimal? MaxPriceTotalCost { get; set; }

    [Name("PriceDifference")]
    public decimal? PriceDifference { get; set; }

    [Name("AvgPrice")]
    public decimal? AvgPrice { get; set; }

    [Name("MinOrderQty")]
    public int MinOrderQty { get; set; } = 1;

    [Name("SiteCostBreakdown")]
    public string SiteCostBreakdown { get; set; } = "";

    [Name("AllProductNames")]
    public string AllProductNames { get; set; } = "";

    [Name("AllStores")]
    public string AllStores { get; set; } = "";

    [Name("AllUrls")]
    public string AllUrls { get; set; } = "";
}