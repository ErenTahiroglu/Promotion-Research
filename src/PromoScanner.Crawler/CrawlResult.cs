using System.Collections.Concurrent;
using PromoScanner.Core;

namespace PromoScanner.Crawler;

/// <summary>
/// Crawl sonuçlarını taşıyan DTO. Thread-safe koleksiyonlar kullanır.
/// </summary>
public sealed class CrawlResult
{
    /// <summary>Tüm toplanan ürün satırları (thread-safe).</summary>
    public ConcurrentBag<ResultRow> AllResults { get; init; } = [];

    public HashSet<string> VisitedUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<string> SkippedUrls { get; init; } = [];
    public ConcurrentBag<string> FailedUrls { get; init; } = [];

    public int Phase1Count { get; set; }
    public int Phase2Count { get; set; }
    public int Phase2Updated { get; set; }

    public Dictionary<string, int> SiteNewCount { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SiteRefreshCount { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
