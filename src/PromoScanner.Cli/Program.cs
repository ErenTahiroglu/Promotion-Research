using System.Globalization;
using System.Text;
using System.Diagnostics;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Playwright;
using PromoScanner.Core;
using PromoScanner.Scraping;

static void Log(string msg) { Console.WriteLine(msg); Debug.WriteLine(msg); }
static string NowStamp() =>
    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

var outDir = Path.Combine(Directory.GetCurrentDirectory(), "out", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(outDir);

var logPath = Path.Combine(outDir, "run.log");
void LogFile(string msg)
{
    var line = $"[{NowStamp()}] {msg}";
    Log(line);
    File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
}

var urlsPath = Path.Combine(AppContext.BaseDirectory, "urls.txt");
if (!File.Exists(urlsPath)) { LogFile($"[ERR] urls.txt bulunamadi: {urlsPath}"); return; }

var seeds = File.ReadAllLines(urlsPath)
    .Select(l => l.Trim())
    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
    .Select(ScraperHelpers.NormalizeUrl)
    .Distinct()
    .ToList();

LogFile($"OUT: {outDir}");
LogFile($"Seed sayisi: {seeds.Count}");

var results = new List<ResultRow>();
var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var skipped = new List<string>();
var failed = new List<string>();
var registry = new ScraperRegistry();

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
await context.RouteAsync("**/*", async route =>
{
    var rt = route.Request.ResourceType;
    if (rt is "image" or "font" or "media" or "stylesheet") await route.AbortAsync();
    else await route.ContinueAsync();
});

var page = await context.NewPageAsync();
page.SetDefaultNavigationTimeout(90_000);
page.SetDefaultTimeout(10_000);

var q = new Queue<(string seed, string url)>();
foreach (var s in seeds) q.Enqueue((s, s));

const int MAX_PAGES = 400;
int processed = 0;

while (q.Count > 0 && processed < MAX_PAGES)
{
    var (seed, url) = q.Dequeue();
    url = ScraperHelpers.NormalizeUrl(url);

    if (visited.Contains(url)) continue;
    visited.Add(url);
    processed++;

    if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
    {
        skipped.Add(url);
        LogFile($"[SKIP] Gecersiz URL: {url}");
        continue;
    }

    if (ScraperHelpers.LooksLikeFileDownload(u))
    {
        skipped.Add(url);
        LogFile($"[SKIP] Dosya: {url}");
        continue;
    }

    LogFile($"[OPEN] {url}");

    try
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
        await page.WaitForTimeoutAsync(500);

        var scraper = registry.FindScraper(u);
        var extracted = scraper != null
            ? await scraper.ExtractAsync(page, u, seed)
            : new List<ResultRow>();

        if (extracted.Count > 0)
        {
            results.AddRange(extracted);
            LogFile($"[OK] {url} - {extracted.Count} urun");
        }
        else
        {
            LogFile($"[INFO] {url} - urun yok, kategoriler aranıyor...");
            var catLinks = await ScraperHelpers.FindCategoryLinksAsync(page, u);

            if (catLinks.Count > 0)
            {
                LogFile($"[INFO] {catLinks.Count} kategori bulundu");
                foreach (var cl in catLinks)
                {
                    if (!visited.Contains(cl)) q.Enqueue((seed, cl));
                }
            }
            else
            {
                LogFile($"[SKIP] {url} - ne urun ne kategori");
            }
        }

        var links = await ScraperHelpers.ExtractSameHostLinksAsync(page, u);
        foreach (var link in links)
        {
            if (!visited.Contains(link)) q.Enqueue((seed, link));
        }
    }
    catch (TimeoutException ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
        LogFile($"[ERR] TIMEOUT: {url}");
    }
    catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase))
    {
        skipped.Add(url);
        LogFile($"[SKIP] Download: {url}");
    }
    catch (Exception ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
        LogFile($"[ERR] {ex.GetType().Name}: {url} - {ex.Message}");
    }
}

File.WriteAllLines(Path.Combine(outDir, "visited_urls.txt"), visited.OrderBy(x => x), Encoding.UTF8);
File.WriteAllLines(Path.Combine(outDir, "skipped_urls.txt"), skipped.Distinct().OrderBy(x => x), Encoding.UTF8);
File.WriteAllLines(Path.Combine(outDir, "failed_urls.txt"), failed.Distinct().OrderBy(x => x), Encoding.UTF8);

var csvCfg = new CsvConfiguration(CultureInfo.GetCultureInfo("tr-TR"))
{
    Delimiter = ";",
    HasHeaderRecord = true
};

void WriteCsv<T>(string path, IEnumerable<T> rows)
{
    using var sw = new StreamWriter(path, false, new UTF8Encoding(true));
    using var csv = new CsvWriter(sw, csvCfg);
    csv.WriteHeader<T>();
    csv.NextRecord();
    foreach (var r in rows)
    {
        csv.WriteRecord(r);
        csv.NextRecord();
    }
}

WriteCsv(Path.Combine(outDir, "results.csv"), results);

var validProducts = results
    .Where(r => string.IsNullOrEmpty(r.Error)
             && !string.IsNullOrWhiteSpace(r.ProductName)
             && r.ProductName.Length >= 5)
    .GroupBy(r => r.Url)
    .Select(g => g.First())
    .ToList();

LogFile($"Ham: {results.Count} - Gecerli: {validProducts.Count}");
WriteCsv(Path.Combine(outDir, "products_valid.csv"), validProducts);

var quoteProducts = validProducts.Where(r => r.RequiresQuote).ToList();
var pricedProducts = validProducts.Where(r => r.Price.HasValue && r.Price > 0).ToList();

if (quoteProducts.Count > 0)
{
    WriteCsv(Path.Combine(outDir, "requires_quote.csv"), quoteProducts);
    LogFile($"Teklif gereken: {quoteProducts.Count}");
}

WriteCsv(Path.Combine(outDir, "products_priced.csv"), pricedProducts);

if (pricedProducts.Count > 0)
{
    LogFile("Akilli karsilastirma yapiliyor...");
    var smartGroups = SmartProductMatcher.GroupSimilarProducts(pricedProducts);
    WriteCsv(Path.Combine(outDir, "smart_comparison.csv"), smartGroups);
    LogFile($"Karsilastirma: {smartGroups.Count} grup, {smartGroups.Count(g => g.SiteCount >= 2)} tanesi 2+ sitede");

    var bestDeals = smartGroups
        .Where(g => g.SiteCount >= 2)
        .OrderByDescending(g => g.PriceDifference ?? 0)
        .Take(50)
        .ToList();

    if (bestDeals.Count > 0)
    {
        WriteCsv(Path.Combine(outDir, "best_deals.csv"), bestDeals);
    }
}

LogFile("===== OZET =====");
LogFile($"Taranan sayfa : {visited.Count}");
LogFile($"Gecerli urun  : {validProducts.Count}");
LogFile($"Fiyatli urun  : {pricedProducts.Count}");
LogFile($"Teklif gereken: {quoteProducts.Count}");
LogFile($"Basarisiz     : {failed.Count}");
LogFile($"Cikti klasoru : {outDir}");