using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using PromoScanner.Core;
using PromoScanner.Scraping;

namespace PromoScanner.Crawler;

/// <summary>
/// Ana crawling motoru. Phase 1 (yeni sayfa keşfi) ve Phase 2 (fiyat güncelleme) çalıştırır.
/// </summary>
public sealed partial class CrawlerEngine : ICrawlerEngine
{
    private readonly IScraperRegistry _registry;
    private readonly IPageCacheManager _cache;
    private readonly IBlacklistManager _blacklist;
    private readonly ScanSettings _settings;
    private readonly ILogger<CrawlerEngine> _logger;

    // Performans: fiyatlı ürün sayacı (O(1) vs O(n) count)
    private int _pricedCounter;

    // Periyodik kaydetme aralığı
    private const int PeriodicSaveInterval = 200;

    public CrawlerEngine(
        IScraperRegistry registry,
        IPageCacheManager cache,
        IBlacklistManager blacklist,
        IOptions<ScanSettings> settings,
        ILogger<CrawlerEngine> logger)
    {
        _registry = registry;
        _cache = cache;
        _blacklist = blacklist;
        _settings = settings.Value;
        _logger = logger;
    }

    [GeneratedRegex(@"/\d{3,}[A-Z]{0,5}$")]
    private static partial Regex DetailCodePattern();

    [GeneratedRegex(@"-\d{2,}$")]
    private static partial Regex TrailingDigitsPattern();

    public async Task<CrawlResult> RunAsync(IReadOnlyList<string> seedUrls, CancellationToken ct = default)
    {
        var result = new CrawlResult();
        var visited = result.VisitedUrls;
        var queue = new CrawlQueue();
        _pricedCounter = 0;

        // Blacklisted URL'leri visited'a ekle
        foreach (var bl in _blacklist.AllUrls) visited.Add(bl);

        // Seed'leri kuyruğa ekle
        var seeds = seedUrls
            .Select(ScraperHelpers.NormalizeUrl)
            .Distinct()
            .ToList();

        // O(1) seed lookup (Phase 1'deki seeds.Any() yerine)
        var seedSet = new HashSet<string>(seeds, StringComparer.OrdinalIgnoreCase);

        foreach (var s in seeds) EnqueueIfNew(queue, visited, s, s, 0);

        _logger.LogInformation("Seed sayısı: {Count}", seeds.Count);

        // Playwright başlat
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = _settings.Headless });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            UserAgent = _settings.UserAgent
        });
        await context.RouteAsync("**/*", async route =>
        {
            var rt = route.Request.ResourceType;
            if (rt is "image" or "font" or "media" or "stylesheet") await route.AbortAsync();
            else await route.ContinueAsync();
        });

        var page = await context.NewPageAsync();
        page.SetDefaultNavigationTimeout(_settings.BrowserTimeoutMs);
        page.SetDefaultTimeout(_settings.ElementTimeoutMs);

        try
        {
            // ===== PHASE 1: YENİ SAYFA KEŞFİ =====
            await RunPhase1Async(page, seeds, seedSet, queue, result, ct);

            // ===== PHASE 2: FİYAT GÜNCELLEME =====
            await RunPhase2Async(page, seeds, result, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Tarama iptal edildi — cache/blacklist kaydediliyor...");
        }
        finally
        {
            // Her koşulda kaydet
            _blacklist.Save();
            _cache.Save();
        }

        return result;
    }

    private async Task RunPhase1Async(
        IPage page, List<string> seeds, HashSet<string> seedSet, CrawlQueue queue, CrawlResult result, CancellationToken ct)
    {
        var visited = result.VisitedUrls;
        int maxPages = _settings.MaxNewPages;
        int maxPerSite = _settings.MaxNewPerSite;

        _logger.LogInformation(
            "===== PHASE 1: Yeni sayfa keşfi (max {Max}, site başına {PerSite}) =====",
            maxPages, maxPerSite);

        while (queue.Count > 0 && result.Phase1Count < maxPages)
        {
            ct.ThrowIfCancellationRequested();

            var next = queue.Dequeue();
            if (next == null) break;
            var (seed, url) = next.Value;

            url = ScraperHelpers.NormalizeUrl(url);
            if (visited.Contains(url)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                result.SkippedUrls.Add(url);
                continue;
            }

            // Cache'te olan sayfalar: seed değilse Phase 2'ye bırak
            if (_cache.Contains(url))
            {
                if (!seedSet.Contains(url)) continue;
            }

            var host = u.Host;
            if (GetSiteCount(result.SiteNewCount, host) >= maxPerSite) continue;

            visited.Add(url);
            result.Phase1Count++;
            IncSiteCount(result.SiteNewCount, host);

            if (ScraperHelpers.LooksLikeFileDownload(u))
            {
                result.SkippedUrls.Add(url);
                continue;
            }

            if (result.Phase1Count % 100 == 0)
                _logger.LogInformation("[P1-DURUM] {Count}/{Max}, {Priced} fiyatlı, kuyruk {Queue}",
                    result.Phase1Count, maxPages, _pricedCounter, queue.Count);

            // Periyodik kaydetme
            if (result.Phase1Count % PeriodicSaveInterval == 0)
            {
                _cache.Save();
                _blacklist.Save();
                _logger.LogDebug("[P1-SAVE] Periyodik cache/blacklist kaydedildi");
            }

            _logger.LogDebug("[P1] {Url}", url);

            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = _settings.BrowserTimeoutMs
                });
                await page.WaitForTimeoutAsync(_settings.PageLoadWaitMs);

                var scraper = _registry.FindScraper(u);
                var extracted = scraper != null
                    ? await scraper.ExtractAsync(page, u, seed)
                    : new List<ResultRow>();

                int pricedCount = 0, quoteCount = 0;
                foreach (var row in extracted)
                {
                    // Tüm ürünleri AllResults'a ekle (quote dahil)
                    result.AllResults.Add(row);

                    if (row.RequiresQuote || (!row.Price.HasValue && string.IsNullOrEmpty(row.Error)))
                        quoteCount++;
                    else if (row.Price > 0)
                    {
                        pricedCount++;
                        _pricedCounter++;
                    }
                }

                // Blacklist kontrolü
                bool isDetailPage = url.Contains("/urun/", StringComparison.OrdinalIgnoreCase) ||
                                    url.Contains("/product/", StringComparison.OrdinalIgnoreCase) ||
                                    DetailCodePattern().IsMatch(url) ||
                                    TrailingDigitsPattern().IsMatch(url);

                if (isDetailPage && pricedCount == 0 && quoteCount > 0)
                    _blacklist.AddConfirmed(url);
                else if (!isDetailPage && pricedCount == 0 && quoteCount >= 3)
                    _blacklist.AddConfirmed(url);

                _cache.Set(new PageCacheEntry
                {
                    Url = url,
                    Store = host,
                    HasProducts = pricedCount > 0,
                    ProductCount = pricedCount,
                    LastVisited = DateTimeOffset.Now
                });

                if (extracted.Count > 0)
                {
                    _logger.LogInformation("[P1-OK] {Url} - {Priced} fiyatlı, {Quote} teklif",
                        url, pricedCount, quoteCount);

                    if (extracted.Count >= 3)
                    {
                        var pagLinks = await ScraperHelpers.FindPaginationLinksAsync(page, u);
                        if (pagLinks.Count > 0)
                        {
                            _logger.LogDebug("[PAGE] {Count} sayfalama", pagLinks.Count);
                            foreach (var pl in pagLinks) EnqueueIfNew(queue, visited, seed, pl, 0);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("[P1-INFO] {Url} - ürün yok, kategoriler...", url);
                    var catLinks = await ScraperHelpers.FindCategoryLinksAsync(page, u);
                    if (catLinks.Count > 0)
                    {
                        _logger.LogDebug("[P1-INFO] {Count} kategori", catLinks.Count);
                        foreach (var cl in catLinks) EnqueueIfNew(queue, visited, seed, cl, 0);
                    }
                    var pagLinks2 = await ScraperHelpers.FindPaginationLinksAsync(page, u);
                    foreach (var pl in pagLinks2) EnqueueIfNew(queue, visited, seed, pl, 0);
                }

                var links = await ScraperHelpers.ExtractSameHostLinksAsync(page, u);
                foreach (var link in links)
                {
                    bool isProduct = link.Contains("/urun/", StringComparison.OrdinalIgnoreCase) ||
                                     link.Contains("/product/", StringComparison.OrdinalIgnoreCase);
                    EnqueueIfNew(queue, visited, seed, link, isProduct ? 0 : 1);
                }
            }
            catch (TimeoutException ex)
            {
                result.FailedUrls.Add(url);
                result.AllResults.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
                _logger.LogWarning("[P1-ERR] TIMEOUT: {Url}", url);
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedUrls.Add(url);
            }
            catch (Exception ex)
            {
                result.FailedUrls.Add(url);
                result.AllResults.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
                _logger.LogWarning("[P1-ERR] {Type}: {Url} - {Msg}", ex.GetType().Name, url, ex.Message);
            }
        }

        _logger.LogInformation("Phase 1 bitti: {Count} sayfa, {Priced} fiyatlı ürün", result.Phase1Count, _pricedCounter);
        _logger.LogInformation("  Site: {Sites}",
            string.Join(", ", result.SiteNewCount
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}:{kv.Value}")));
    }

    private async Task RunPhase2Async(
        IPage page, List<string> seeds, CrawlResult result, CancellationToken ct)
    {
        var visited = result.VisitedUrls;
        var refreshCandidates = _cache.GetRefreshCandidates(visited, _blacklist);

        if (refreshCandidates.Count == 0) return;

        int maxPages = _settings.MaxRefreshPages;
        int maxPerSite = _settings.MaxRefreshPerSite;

        _logger.LogInformation(
            "===== PHASE 2: Fiyat güncelleme ({Candidates} aday, max {Max}) =====",
            refreshCandidates.Count, maxPages);

        foreach (var cached in refreshCandidates)
        {
            ct.ThrowIfCancellationRequested();

            if (result.Phase2Count >= maxPages) break;

            var url = cached.Url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) continue;

            var host = u.Host;
            if (GetSiteCount(result.SiteRefreshCount, host) >= maxPerSite) continue;

            visited.Add(url);
            result.Phase2Count++;
            IncSiteCount(result.SiteRefreshCount, host);

            if (result.Phase2Count % 100 == 0)
                _logger.LogInformation("[P2-DURUM] {Count}/{Max}", result.Phase2Count, maxPages);

            // Periyodik kaydetme
            if (result.Phase2Count % PeriodicSaveInterval == 0)
            {
                _cache.Save();
                _logger.LogDebug("[P2-SAVE] Periyodik cache kaydedildi");
            }

            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = _settings.BrowserTimeoutMs
                });
                await page.WaitForTimeoutAsync(_settings.PageLoadWaitMs);

                var seedUrl = seeds.FirstOrDefault(s =>
                    Uri.TryCreate(s, UriKind.Absolute, out var su) &&
                    string.Equals(su.Host, host, StringComparison.OrdinalIgnoreCase)) ?? url;

                var scraper = _registry.FindScraper(u);
                var extracted = scraper != null
                    ? await scraper.ExtractAsync(page, u, seedUrl)
                    : new List<ResultRow>();

                int pricedCount = 0;
                foreach (var row in extracted)
                {
                    if (!row.RequiresQuote && row.Price.HasValue && row.Price > 0)
                    {
                        pricedCount++;
                        _pricedCounter++;
                        result.AllResults.Add(row);
                    }
                }

                if (pricedCount > 0)
                {
                    result.Phase2Updated++;
                    if (result.Phase2Updated % 50 == 0 || result.Phase2Updated <= 3)
                        _logger.LogInformation("[P2-OK] {Url} - {Count} fiyat", url, pricedCount);
                }

                _cache.Set(new PageCacheEntry
                {
                    Url = url,
                    Store = host,
                    HasProducts = pricedCount > 0,
                    ProductCount = pricedCount,
                    LastVisited = DateTimeOffset.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[P2-ERR] {Url} - {Msg}", url, ex.Message);
            }
        }

        _logger.LogInformation("Phase 2 bitti: {Count} sayfa, {Updated} güncelleme",
            result.Phase2Count, result.Phase2Updated);
    }

    // -- Yardımcı metotlar --
    private void EnqueueIfNew(CrawlQueue queue, HashSet<string> visited, string seed, string url, int priority)
    {
        url = ScraperHelpers.NormalizeUrl(url);
        if (_blacklist.IsBlacklisted(url)) return;
        if (visited.Contains(url)) return;
        queue.Enqueue(seed, url, priority);
    }

    private static int GetSiteCount(Dictionary<string, int> dict, string host)
        => dict.TryGetValue(host, out var c) ? c : 0;

    private static void IncSiteCount(Dictionary<string, int> dict, string host)
        => dict[host] = GetSiteCount(dict, host) + 1;
}
