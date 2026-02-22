using CsvHelper.Configuration.Attributes;

namespace PromoScanner.Core;

/// <summary>
/// Birden fazla sitedeki benzer ürünleri temsil eden karşılaştırma grubu.
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

    [Name("MaxPrice")]
    public decimal? MaxPrice { get; set; }

    [Name("MaxPriceStore")]
    public string MaxPriceStore { get; set; } = "";

    [Name("MaxPriceUrl")]
    public string MaxPriceUrl { get; set; } = "";

    [Name("PriceDifference")]
    public decimal? PriceDifference { get; set; }

    [Name("AvgPrice")]
    public decimal? AvgPrice { get; set; }

    [Name("MinOrderQty")]
    public int MinOrderQty { get; set; }

    [Name("AllProductNames")]
    public string AllProductNames { get; set; } = "";

    [Name("AllStores")]
    public string AllStores { get; set; } = "";

    [Name("AllUrls")]
    public string AllUrls { get; set; } = "";
}