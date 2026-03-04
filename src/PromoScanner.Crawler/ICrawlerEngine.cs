using PromoScanner.Core;

namespace PromoScanner.Crawler;

/// <summary>
/// Crawling motorunun ana arayüzü.
/// </summary>
public interface ICrawlerEngine
{
    /// <summary>
    /// Phase 1 (keşif) ve Phase 2 (güncelleme) taramalarını çalıştırır.
    /// </summary>
    Task<CrawlResult> RunAsync(IReadOnlyList<string> seedUrls, CancellationToken ct = default);
}
