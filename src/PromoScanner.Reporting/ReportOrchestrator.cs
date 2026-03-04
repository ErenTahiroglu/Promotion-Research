using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromoScanner.Core;

namespace PromoScanner.Reporting;

/// <summary>
/// Tüm rapor formatlarını (Excel, CSV, URL listeleri) tek seferde üreten orchestrator.
/// SmartProductMatcher çağrısını da burada yapar.
/// </summary>
public sealed class ReportOrchestrator : IReportWriter
{
    private readonly ILogger<ReportOrchestrator> _logger;
    private readonly ScanSettings _settings;

    public ReportOrchestrator(ILogger<ReportOrchestrator> logger, IOptions<ScanSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public void WriteReports(ReportData data, string outputDir)
    {
        var totalSw = Stopwatch.StartNew();
        Directory.CreateDirectory(outputDir);

        // URL listeleri
        File.WriteAllLines(Path.Combine(outputDir, "visited_urls.txt"),
            data.VisitedUrls.OrderBy(x => x), Encoding.UTF8);
        File.WriteAllLines(Path.Combine(outputDir, "skipped_urls.txt"),
            data.SkippedUrls.Distinct().OrderBy(x => x), Encoding.UTF8);
        File.WriteAllLines(Path.Combine(outputDir, "failed_urls.txt"),
            data.FailedUrls.Distinct().OrderBy(x => x), Encoding.UTF8);

        // Ham sonuçlar CSV
        CsvReportWriter.WriteCsv(Path.Combine(outputDir, "results.csv"), data.AllResults);

        // Geçerli ürünler filtresi — (Store + NormalizedName) bazlı dedup
        var validProducts = data.AllResults
            .Where(r => string.IsNullOrEmpty(r.Error)
                         && !string.IsNullOrWhiteSpace(r.ProductName)
                         && r.ProductName.Length >= 5)
            .GroupBy(r => (r.Store, NormName: SmartProductMatcher.Normalize(r.ProductName)))
            .Select(g =>
            {
                var priced = g.FirstOrDefault(r => r.Price.HasValue && r.Price > 0);
                return priced ?? g.First();
            })
            .ToList();

        var quoteProducts = validProducts
            .Where(r => r.RequiresQuote || (!r.Price.HasValue && string.IsNullOrEmpty(r.Error)))
            .ToList();
        var pricedProducts = validProducts
            .Where(r => r.Price.HasValue && r.Price > 0 && !r.RequiresQuote)
            .ToList();

        _logger.LogInformation("Ham: {Raw} - Geçerli: {Valid}", data.AllResults.Count, validProducts.Count);
        _logger.LogInformation("Fiyatlı: {Priced}, Teklif: {Quote}", pricedProducts.Count, quoteProducts.Count);

        CsvReportWriter.WriteCsv(Path.Combine(outputDir, "products_valid.csv"), validProducts);
        CsvReportWriter.WriteCsv(Path.Combine(outputDir, "products_priced.csv"), pricedProducts);
        if (quoteProducts.Count > 0)
            CsvReportWriter.WriteCsv(Path.Combine(outputDir, "requires_quote.csv"), quoteProducts);

        // Akıllı karşılaştırma (KDV oranıyla)
        List<SmartProductGroup> smartGroups = [];
        int crossSiteCount = 0;

        if (pricedProducts.Count > 0)
        {
            _logger.LogInformation("Akıllı karşılaştırma yapılıyor (KDV: %{KdvRate})...",
                _settings.KdvRate * 100);
            var matchSw = Stopwatch.StartNew();
            smartGroups = SmartProductMatcher.GroupSimilarProducts(pricedProducts, _settings.KdvRate);
            matchSw.Stop();

            CsvReportWriter.WriteCsv(Path.Combine(outputDir, "smart_comparison.csv"), smartGroups);

            crossSiteCount = smartGroups.Count(g => g.SiteCount >= 2);
            _logger.LogInformation("Karşılaştırma: {Groups} grup, {Cross} tanesi 2+ sitede ({Elapsed}ms)",
                smartGroups.Count, crossSiteCount, matchSw.ElapsedMilliseconds);

            var bestDeals = smartGroups
                .Where(g => g.SiteCount >= 2 && g.PriceDifference > 0)
                .OrderByDescending(g => (g.MaxPriceTotalCost ?? 0) - (g.MinPriceTotalCost ?? 0)) // Toplam maliyet farkı
                .Take(50)
                .ToList();

            if (bestDeals.Count > 0)
                CsvReportWriter.WriteCsv(Path.Combine(outputDir, "best_deals.csv"), bestDeals);
        }

        // Quote ürünlerin fiyatlı alternatifleri
        var quoteAlternatives = new List<QuoteAlternative>();
        if (quoteProducts.Count > 0 && smartGroups.Count > 0)
        {
            quoteAlternatives = FindQuoteAlternatives(quoteProducts, pricedProducts);
            if (quoteAlternatives.Count > 0)
                CsvReportWriter.WriteCsv(Path.Combine(outputDir, "quote_with_alternatives.csv"), quoteAlternatives);
        }

        // Excel raporu
        _logger.LogInformation("Excel raporu yazılıyor...");
        var excelSw = Stopwatch.StartNew();
        var excelPath = Path.Combine(outputDir, "PromoScanner_Rapor.xlsx");
        ExcelReportWriter.Write(excelPath, validProducts, pricedProducts, quoteProducts, smartGroups, quoteAlternatives);
        excelSw.Stop();
        _logger.LogInformation("Excel: {Path} ({Elapsed}ms)", excelPath, excelSw.ElapsedMilliseconds);

        // Özet log
        totalSw.Stop();
        _logger.LogInformation("===== ÖZET =====");
        _logger.LogInformation("Phase 1 (keşif) : {P1} sayfa", data.Phase1Count);
        _logger.LogInformation("Phase 2 (fiyat) : {P2} sayfa ({Updated} güncellendi)", data.Phase2Count, data.Phase2Updated);
        _logger.LogInformation("Geçerli ürün     : {Valid}", validProducts.Count);
        _logger.LogInformation("Fiyatlı ürün     : {Priced}", pricedProducts.Count);
        _logger.LogInformation("Teklif gereken   : {Quote}", quoteProducts.Count);
        _logger.LogInformation("Karşılaştırma    : {Groups} grup ({CrossSite} cross-site)",
            smartGroups.Count, crossSiteCount);
        _logger.LogInformation("Başarısız        : {Failed}", data.FailedUrls.Count);
        _logger.LogInformation("Rapor süresi     : {Elapsed}ms", totalSw.ElapsedMilliseconds);
        _logger.LogInformation("Çıktı klasörü    : {Dir}", outputDir);

        var siteReport = pricedProducts
            .GroupBy(p => p.Store)
            .Select(g => $"  {g.Key}: {g.Count()} fiyatlı")
            .OrderByDescending(x => x);
        foreach (var sr in siteReport) _logger.LogInformation("{Report}", sr);
    }

    /// <summary>
    /// Quote ürünleri fiyatlı ürünlerle eşleştirir (normalized isim benzerliği ile).
    /// </summary>
    private static List<QuoteAlternative> FindQuoteAlternatives(
        List<ResultRow> quoteProducts, List<ResultRow> pricedProducts)
    {
        var results = new List<QuoteAlternative>();
        var pricedByNorm = pricedProducts
            .GroupBy(p => SmartProductMatcher.Normalize(p.ProductName))
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Price).First());

        foreach (var q in quoteProducts)
        {
            var normName = SmartProductMatcher.Normalize(q.ProductName);
            // Tam eşleşme veya kısmi token eşleşme
            if (pricedByNorm.TryGetValue(normName, out var match))
            {
                results.Add(new QuoteAlternative
                {
                    QuoteStore = q.Store,
                    QuoteProductName = q.ProductName,
                    QuoteUrl = q.Url,
                    AlternativeStore = match.Store,
                    AlternativePrice = match.Price ?? 0,
                    AlternativeCurrency = match.Currency,
                    AlternativeUrl = match.Url
                });
            }
        }
        return results;
    }
}

/// <summary>
/// Teklif gereken ürünlerin fiyatlı alternatiflerini temsil eder.
/// </summary>
public sealed class QuoteAlternative
{
    public string QuoteStore { get; init; } = "";
    public string QuoteProductName { get; init; } = "";
    public string QuoteUrl { get; init; } = "";
    public string AlternativeStore { get; init; } = "";
    public decimal AlternativePrice { get; init; }
    public string AlternativeCurrency { get; init; } = "";
    public string AlternativeUrl { get; init; } = "";
}
