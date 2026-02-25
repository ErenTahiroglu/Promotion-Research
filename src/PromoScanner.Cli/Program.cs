using System.Globalization;
using System.Text;
using System.Diagnostics;
using ClosedXML.Excel;
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

// ===== AYARLAR =====
const int MAX_NEW_PAGES = 1500;
const int MAX_NEW_PER_SITE = 300;
const int MAX_REFRESH_PAGES = 800;
const int MAX_REFRESH_PER_SITE = 200;

// ===== QUOTE BLACKLIST =====
// SADECE gercekten ziyaret edilip fiyat bulunamayan URL'ler
var dataDir = AppContext.BaseDirectory;
var blacklistPath = Path.Combine(dataDir, "quote_blacklist.txt");
var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

if (File.Exists(blacklistPath))
{
    foreach (var l in File.ReadAllLines(blacklistPath)
        .Select(l => l.Trim())
        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
        blacklist.Add(l);
    LogFile($"Blacklist yuklendi: {blacklist.Count} URL atlanacak");
}

// ===== PAGE CACHE =====
var cachePath = Path.Combine(dataDir, "page_cache.csv");
var pageCacheDict = new Dictionary<string, PageCacheEntry>(StringComparer.OrdinalIgnoreCase);

if (File.Exists(cachePath))
{
    foreach (var line in File.ReadAllLines(cachePath).Skip(1))
    {
        var parts = line.Split(';');
        if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[0]))
        {
            pageCacheDict[parts[0].Trim()] = new PageCacheEntry
            {
                Url = parts[0].Trim(),
                Store = parts[1].Trim(),
                HasProducts = parts[2].Trim() == "1",
                ProductCount = int.TryParse(parts[3].Trim(), out var pc) ? pc : 0
            };
        }
    }
    LogFile($"Cache yuklendi: {pageCacheDict.Count} sayfa ({pageCacheDict.Count(kv => kv.Value.HasProducts)} urunlu)");
}

var urlsPath = Path.Combine(dataDir, "urls.txt");
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
// ONEMLI: Sadece gercekten ziyaret edilip fiyatsiz bulunan URL'ler blacklist'e eklenir
var confirmedQuoteUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var registry = new ScraperRegistry();

foreach (var bl in blacklist) visited.Add(bl);

var siteNewCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var siteRefreshCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

int GetSiteCount(Dictionary<string, int> dict, string host)
    => dict.TryGetValue(host, out var c) ? c : 0;
void IncSiteCount(Dictionary<string, int> dict, string host)
    => dict[host] = GetSiteCount(dict, host) + 1;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
await context.RouteAsync("**/*", async route =>
{
    var rt = route.Request.ResourceType;
    if (rt is "image" or "font" or "media" or "stylesheet") await route.AbortAsync();
    else await route.ContinueAsync();
});

var page = await context.NewPageAsync();
page.SetDefaultNavigationTimeout(90_000);
page.SetDefaultTimeout(10_000);

// -- Oncelikli kuyruk --
var q = new SortedList<int, Queue<(string seed, string url)>>();
void Enqueue(string seed, string url, int priority = 1)
{
    url = ScraperHelpers.NormalizeUrl(url);
    if (blacklist.Contains(url)) return;
    if (visited.Contains(url)) return;
    if (!q.ContainsKey(priority)) q[priority] = new Queue<(string seed, string url)>();
    q[priority].Enqueue((seed, url));
}

(string seed, string url)? Dequeue()
{
    foreach (var kv in q)
        if (kv.Value.Count > 0) return kv.Value.Dequeue();
    return null;
}

int QueueCount() => q.Values.Sum(qu => qu.Count);

foreach (var s in seeds) Enqueue(s, s, 0);

int phase1Count = 0;

// ===== PHASE 1: YENI SAYFA KESFI =====
LogFile($"===== PHASE 1: Yeni sayfa kesfi (max {MAX_NEW_PAGES}, site basina {MAX_NEW_PER_SITE}) =====");

while (QueueCount() > 0 && phase1Count < MAX_NEW_PAGES)
{
    var next = Dequeue();
    if (next == null) break;
    var (seed, url) = next.Value;

    url = ScraperHelpers.NormalizeUrl(url);
    if (visited.Contains(url)) continue;

    if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
    {
        skipped.Add(url);
        continue;
    }

    // Cache'te olan sayfalar: seed degilse Phase 2'ye birak
    if (pageCacheDict.ContainsKey(url))
    {
        bool isSeed = seeds.Any(s => string.Equals(s, url, StringComparison.OrdinalIgnoreCase));
        if (!isSeed) continue;
    }

    var host = u.Host;
    if (GetSiteCount(siteNewCount, host) >= MAX_NEW_PER_SITE)
        continue;

    visited.Add(url);
    phase1Count++;
    IncSiteCount(siteNewCount, host);

    if (ScraperHelpers.LooksLikeFileDownload(u))
    {
        skipped.Add(url);
        continue;
    }

    if (phase1Count % 100 == 0)
        LogFile($"[P1-DURUM] {phase1Count}/{MAX_NEW_PAGES}, {results.Count(r => r.Price > 0)} fiyatli, kuyruk {QueueCount()}");

    LogFile($"[P1] {url}");

    try
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
        await page.WaitForTimeoutAsync(500);

        var scraper = registry.FindScraper(u);
        var extracted = scraper != null
            ? await scraper.ExtractAsync(page, u, seed)
            : new List<ResultRow>();

        int pricedCount = 0, quoteCount = 0;

        foreach (var row in extracted)
        {
            if (row.RequiresQuote || (!row.Price.HasValue && string.IsNullOrEmpty(row.Error)))
            {
                quoteCount++;
                // KRITIK: Listing sayfadaki urunlerin detay URL'leri BLACKLIST'e eklenmez!
                // Sadece BU SAYFA ziyaret edildi ve sonuc teklif ise, BU URL blacklist'e gider
            }
            else
            {
                pricedCount++;
                results.Add(row);
            }
        }

        // Eger bu sayfa detay sayfasiysa ve hic fiyatli urun yoksa → blacklist'e ekle
        bool isDetailPage = url.Contains("/urun/", StringComparison.OrdinalIgnoreCase) ||
                            url.Contains("/product/", StringComparison.OrdinalIgnoreCase) ||
                            System.Text.RegularExpressions.Regex.IsMatch(url, @"/\d{3,}[A-Z]{0,5}$") ||
                            System.Text.RegularExpressions.Regex.IsMatch(url, @"-\d{2,}$");

        if (isDetailPage && pricedCount == 0 && quoteCount > 0)
        {
            confirmedQuoteUrls.Add(url);
        }
        // Listing sayfasi tamamen teklif ise (0 fiyatli, 3+ teklif) → bu listing URL'yi blacklist'e
        else if (!isDetailPage && pricedCount == 0 && quoteCount >= 3)
        {
            confirmedQuoteUrls.Add(url);
        }

        pageCacheDict[url] = new PageCacheEntry
        {
            Url = url,
            Store = host,
            HasProducts = pricedCount > 0,
            ProductCount = pricedCount
        };

        if (extracted.Count > 0)
        {
            LogFile($"[P1-OK] {url} - {pricedCount} fiyatli, {quoteCount} teklif");

            if (extracted.Count >= 3)
            {
                var pagLinks = await ScraperHelpers.FindPaginationLinksAsync(page, u);
                if (pagLinks.Count > 0)
                {
                    LogFile($"[PAGE] {pagLinks.Count} sayfalama");
                    foreach (var pl in pagLinks) Enqueue(seed, pl, 0);
                }
            }
        }
        else
        {
            LogFile($"[P1-INFO] {url} - urun yok, kategoriler...");
            var catLinks = await ScraperHelpers.FindCategoryLinksAsync(page, u);
            if (catLinks.Count > 0)
            {
                LogFile($"[P1-INFO] {catLinks.Count} kategori");
                foreach (var cl in catLinks) Enqueue(seed, cl, 0);
            }
            var pagLinks2 = await ScraperHelpers.FindPaginationLinksAsync(page, u);
            foreach (var pl in pagLinks2) Enqueue(seed, pl, 0);
        }

        var links = await ScraperHelpers.ExtractSameHostLinksAsync(page, u);
        foreach (var link in links)
        {
            bool isProduct = link.Contains("/urun/", StringComparison.OrdinalIgnoreCase) ||
                             link.Contains("/product/", StringComparison.OrdinalIgnoreCase);
            Enqueue(seed, link, isProduct ? 0 : 1);
        }
    }
    catch (TimeoutException ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
        LogFile($"[P1-ERR] TIMEOUT: {url}");
    }
    catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase))
    {
        skipped.Add(url);
    }
    catch (Exception ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
        LogFile($"[P1-ERR] {ex.GetType().Name}: {url} - {ex.Message}");
    }
}

LogFile($"Phase 1 bitti: {phase1Count} sayfa");
LogFile($"  Site: {string.Join(", ", siteNewCount.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}"))}");

// ===== PHASE 2: FIYAT GUNCELLEME =====
var refreshCandidates = pageCacheDict.Values
    .Where(c => c.HasProducts && c.ProductCount > 0)
    .Where(c => !visited.Contains(c.Url) && !blacklist.Contains(c.Url))
    .OrderByDescending(c => c.ProductCount)
    .ToList();

int phase2Count = 0, phase2Updated = 0;

if (refreshCandidates.Count > 0)
{
    LogFile($"===== PHASE 2: Fiyat guncelleme ({refreshCandidates.Count} aday, max {MAX_REFRESH_PAGES}) =====");

    foreach (var cached in refreshCandidates)
    {
        if (phase2Count >= MAX_REFRESH_PAGES) break;

        var url = cached.Url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) continue;

        var host = u.Host;
        if (GetSiteCount(siteRefreshCount, host) >= MAX_REFRESH_PER_SITE) continue;

        visited.Add(url);
        phase2Count++;
        IncSiteCount(siteRefreshCount, host);

        if (phase2Count % 100 == 0)
            LogFile($"[P2-DURUM] {phase2Count}/{MAX_REFRESH_PAGES}");

        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90_000 });
            await page.WaitForTimeoutAsync(300);

            var seedUrl = seeds.FirstOrDefault(s =>
                Uri.TryCreate(s, UriKind.Absolute, out var su) &&
                string.Equals(su.Host, host, StringComparison.OrdinalIgnoreCase)) ?? url;

            var scraper = registry.FindScraper(u);
            var extracted = scraper != null
                ? await scraper.ExtractAsync(page, u, seedUrl)
                : new List<ResultRow>();

            int pricedCount = 0;
            foreach (var row in extracted)
            {
                if (!row.RequiresQuote && row.Price.HasValue && row.Price > 0)
                {
                    pricedCount++;
                    results.Add(row);
                }
            }

            if (pricedCount > 0)
            {
                phase2Updated++;
                if (phase2Updated % 50 == 0 || phase2Updated <= 3)
                    LogFile($"[P2-OK] {url} - {pricedCount} fiyat");
            }

            pageCacheDict[url] = new PageCacheEntry
            {
                Url = url,
                Store = host,
                HasProducts = pricedCount > 0,
                ProductCount = pricedCount
            };
        }
        catch (Exception ex)
        {
            LogFile($"[P2-ERR] {url} - {ex.Message}");
        }
    }

    LogFile($"Phase 2 bitti: {phase2Count} sayfa, {phase2Updated} guncelleme");
}

// ===== BLACKLIST GUNCELLE (sadece dogrulanmis teklif URL'leri) =====
{
    var allBl = new HashSet<string>(blacklist, StringComparer.OrdinalIgnoreCase);
    foreach (var qu in confirmedQuoteUrls) allBl.Add(qu);

    if (allBl.Count > 0)
    {
        var blContent = new List<string>
        {
            $"# PromoScanner Quote Blacklist - {DateTimeOffset.Now:dd.MM.yyyy HH:mm}",
            $"# {allBl.Count} URL (sadece ziyaret edilip fiyatsiz bulunan sayfalar)"
        };
        blContent.AddRange(allBl.OrderBy(x => x));
        File.WriteAllLines(blacklistPath, blContent, Encoding.UTF8);
    }

    var newBlCount = confirmedQuoteUrls.Count(nqu => !blacklist.Contains(nqu));
    LogFile($"Blacklist guncellendi: {allBl.Count} toplam ({newBlCount} yeni dogrulanmis)");
}

// ===== CACHE KAYDET =====
{
    var sb = new StringBuilder();
    sb.AppendLine("URL;Store;HasProducts;ProductCount");
    foreach (var kv in pageCacheDict.OrderBy(kv => kv.Key))
    {
        var e = kv.Value;
        sb.AppendLine($"{e.Url};{e.Store};{(e.HasProducts ? "1" : "0")};{e.ProductCount}");
    }
    File.WriteAllText(cachePath, sb.ToString(), Encoding.UTF8);
    LogFile($"Cache kaydedildi: {pageCacheDict.Count} sayfa");
}

// -- Dosya ciktilari --
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
    foreach (var r in rows) { csv.WriteRecord(r); csv.NextRecord(); }
}

WriteCsv(Path.Combine(outDir, "results.csv"), results);

var validProducts = results
    .Where(r => string.IsNullOrEmpty(r.Error)
             && !string.IsNullOrWhiteSpace(r.ProductName)
             && r.ProductName.Length >= 5)
    .GroupBy(r => r.Url)
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

LogFile($"Ham: {results.Count} - Gecerli: {validProducts.Count}");
LogFile($"Fiyatli: {pricedProducts.Count}, Teklif: {quoteProducts.Count}");

WriteCsv(Path.Combine(outDir, "products_valid.csv"), validProducts);
WriteCsv(Path.Combine(outDir, "products_priced.csv"), pricedProducts);
if (quoteProducts.Count > 0)
    WriteCsv(Path.Combine(outDir, "requires_quote.csv"), quoteProducts);

List<SmartProductGroup> smartGroups = new();

if (pricedProducts.Count > 0)
{
    LogFile("Akilli karsilastirma yapiliyor...");
    smartGroups = SmartProductMatcher.GroupSimilarProducts(pricedProducts);
    WriteCsv(Path.Combine(outDir, "smart_comparison.csv"), smartGroups);

    var crossSite = smartGroups.Count(g => g.SiteCount >= 2);
    LogFile($"Karsilastirma: {smartGroups.Count} grup, {crossSite} tanesi 2+ sitede");

    var bestDeals = smartGroups
        .Where(g => g.SiteCount >= 2 && g.PriceDifference > 0)
        .OrderByDescending(g => g.PriceDifference ?? 0)
        .Take(50)
        .ToList();

    if (bestDeals.Count > 0)
        WriteCsv(Path.Combine(outDir, "best_deals.csv"), bestDeals);
}

// -- Excel --
LogFile("Excel raporu yaziliyor...");
var excelPath = Path.Combine(outDir, "PromoScanner_Rapor.xlsx");
ExcelReportWriter.Write(excelPath, validProducts, pricedProducts, quoteProducts, smartGroups);
LogFile($"Excel: {excelPath}");

// -- Ozet --
LogFile("===== OZET =====");
LogFile($"Phase 1 (kesif) : {phase1Count} sayfa");
LogFile($"Phase 2 (fiyat) : {phase2Count} sayfa ({phase2Updated} guncellendi)");
LogFile($"Toplam taranan  : {phase1Count + phase2Count}");
LogFile($"Blacklist       : {blacklist.Count + confirmedQuoteUrls.Count(nqu => !blacklist.Contains(nqu))} URL (dogrulanmis)");
LogFile($"Cache           : {pageCacheDict.Count} sayfa");
LogFile($"Gecerli urun    : {validProducts.Count}");
LogFile($"Fiyatli urun    : {pricedProducts.Count}");
LogFile($"Teklif gereken  : {quoteProducts.Count}");
LogFile($"Karsilastirma   : {smartGroups.Count} grup ({smartGroups.Count(g => g.SiteCount >= 2)} cross-site)");
LogFile($"Basarisiz       : {failed.Count}");
LogFile($"Cikti klasoru   : {outDir}");

var siteReport = pricedProducts
    .GroupBy(p => p.Store)
    .Select(g => $"  {g.Key}: {g.Count()} fiyatli")
    .OrderByDescending(x => x);
foreach (var sr in siteReport) LogFile(sr);

// ===== MODELLER =====
record PageCacheEntry
{
    public string Url { get; init; } = "";
    public string Store { get; init; } = "";
    public bool HasProducts { get; init; }
    public int ProductCount { get; init; }
}

// ===== EXCEL RAPORU =====
static class ExcelReportWriter
{
    public static void Write(string filePath, List<ResultRow> valid, List<ResultRow> priced,
        List<ResultRow> quote, List<SmartProductGroup> groups)
    {
        using var wb = new XLWorkbook();
        var hBg = XLColor.FromHtml("#1F4E79");
        var alt = XLColor.FromHtml("#EBF3FB");
        var deal = XLColor.FromHtml("#E2EFDA");
        var warn = XLColor.FromHtml("#FFF2CC");
        var qBg = XLColor.FromHtml("#FCE4D6");

        // Ozet
        {
            var ws = wb.Worksheets.Add("Ozet");
            ws.Cell("B2").Value = "PromoScanner - Tarama Ozeti";
            ws.Cell("B2").Style.Font.FontSize = 20; ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("B2").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
            ws.Range("B2:G2").Merge();
            ws.Cell("B3").Value = $"Olusturulma: {DateTimeOffset.Now:dd.MM.yyyy HH:mm}";
            ws.Cell("B3").Style.Font.Italic = true; ws.Cell("B3").Style.Font.FontColor = XLColor.Gray;
            int r = 5;
            ws.Cell(r, 2).Value = "Metrik"; ws.Cell(r, 3).Value = "Deger"; ws.Row(r).Style.Font.Bold = true;
            var st = new[] { ("Gecerli Urun",valid.Count), ("Fiyatli Urun",priced.Count),
                ("Teklif Gereken",quote.Count), ("Karsilastirma",groups.Count),
                ("2+ Site Eslesme",groups.Count(g=>g.SiteCount>=2)) };
            for (int i = 0; i < st.Length; i++) { ws.Cell(r + 1 + i, 2).Value = st[i].Item1; ws.Cell(r + 1 + i, 3).Value = st[i].Item2; ws.Cell(r + 1 + i, 3).Style.Font.Bold = true; }
            ws.Cell(r, 5).Value = "Site"; ws.Cell(r, 6).Value = "Urun"; ws.Cell(r, 7).Value = "Fiyatli";
            var ss = valid.GroupBy(p => p.Store).Select(g => (g.Key, g.Count(), g.Count(x => x.Price > 0))).OrderByDescending(x => x.Item2).ToList();
            for (int i = 0; i < ss.Count; i++) { ws.Cell(r + 1 + i, 5).Value = ss[i].Item1; ws.Cell(r + 1 + i, 6).Value = ss[i].Item2; ws.Cell(r + 1 + i, 7).Value = ss[i].Item3; }
            ws.Columns().AdjustToContents();
        }

        // Tum Urunler
        {
            var ws = wb.Worksheets.Add("Tum Urunler"); ws.SheetView.FreezeRows(1);
            var h = new[] { "Magaza", "Kategori", "Urun", "Fiyat", "PB", "KDV", "Min.Sip.", "URL", "Zaman" };
            Hdr(ws, 1, h, hBg);
            for (int i = 0; i < priced.Count; i++)
            {
                var rw = priced[i]; int r = i + 2;
                ws.Cell(r, 1).Value = rw.Store; ws.Cell(r, 2).Value = rw.Category; ws.Cell(r, 3).Value = rw.ProductName;
                ws.Cell(r, 4).Value = rw.Price.HasValue ? (double)rw.Price.Value : 0; ws.Cell(r, 5).Value = rw.Currency;
                ws.Cell(r, 6).Value = rw.HasKDV ? "Evet" : "Hayir"; ws.Cell(r, 7).Value = rw.MinOrderQty;
                ws.Cell(r, 8).Value = rw.Url; ws.Cell(r, 9).Value = rw.Timestamp.ToString("dd.MM.yyyy HH:mm");
                ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                if (Uri.TryCreate(rw.Url, UriKind.Absolute, out var u)) ws.Cell(r, 8).SetHyperlink(new XLHyperlink(u.ToString()));
                if (i % 2 == 1) RowBg(ws, r, h.Length, alt);
            }
            Tbl(ws, 1, priced.Count + 1, h.Length); ws.Columns().AdjustToContents(); ws.Column(8).Width = 50;
        }

        // Karsilastirma
        {
            var ws = wb.Worksheets.Add("Karsilastirma"); ws.SheetView.FreezeRows(1);
            var h = new[]{"Kategori","Kapasite","Ozellik","#","Site#",
                "Min Fiyat","Min Site","Min Adet","Min Toplam",
                "Max Fiyat","Max Site","Max Adet","Max Toplam",
                "Birim Fark","Ort","Site Maliyet Detayi","Urunler","Siteler"};
            Hdr(ws, 1, h, hBg);
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i]; int r = i + 2;
                ws.Cell(r, 1).Value = g.Category; ws.Cell(r, 2).Value = g.Capacity; ws.Cell(r, 3).Value = g.KeyFeatures;
                ws.Cell(r, 4).Value = g.ProductCount; ws.Cell(r, 5).Value = g.SiteCount;
                ws.Cell(r, 6).Value = g.MinPrice.HasValue ? (double)g.MinPrice.Value : 0;
                ws.Cell(r, 7).Value = g.MinPriceStore;
                ws.Cell(r, 8).Value = g.MinPriceMinQty;
                ws.Cell(r, 9).Value = g.MinPriceTotalCost.HasValue ? (double)g.MinPriceTotalCost.Value : 0;
                ws.Cell(r, 10).Value = g.MaxPrice.HasValue ? (double)g.MaxPrice.Value : 0;
                ws.Cell(r, 11).Value = g.MaxPriceStore;
                ws.Cell(r, 12).Value = g.MaxPriceMinQty;
                ws.Cell(r, 13).Value = g.MaxPriceTotalCost.HasValue ? (double)g.MaxPriceTotalCost.Value : 0;
                ws.Cell(r, 14).Value = g.PriceDifference.HasValue ? (double)g.PriceDifference.Value : 0;
                ws.Cell(r, 15).Value = g.AvgPrice.HasValue ? (double)g.AvgPrice.Value : 0;
                ws.Cell(r, 16).Value = g.SiteCostBreakdown;
                ws.Cell(r, 17).Value = g.AllProductNames; ws.Cell(r, 18).Value = g.AllStores;
                foreach (int c in new[] { 6, 9, 10, 13, 14, 15 }) ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
                if (g.SiteCount >= 2 && g.PriceDifference > 50) RowBg(ws, r, h.Length, warn);
                else if (g.SiteCount >= 2) RowBg(ws, r, h.Length, deal);
                else if (i % 2 == 1) RowBg(ws, r, h.Length, alt);
            }
            Tbl(ws, 1, groups.Count + 1, h.Length); ws.Columns().AdjustToContents();
            ws.Column(16).Width = 80; ws.Column(17).Width = 60;
        }

        // En Iyi Firsatlar
        {
            var ds = groups.Where(g => g.SiteCount >= 2 && g.PriceDifference > 0).OrderByDescending(g => g.PriceDifference).Take(50).ToList();
            var ws = wb.Worksheets.Add("En Iyi Firsatlar"); ws.SheetView.FreezeRows(1);
            var h = new[]{"#","Kategori","Kapasite",
                "Min Fiyat","Min Site","Min Adet","Min Toplam",
                "Max Fiyat","Max Site","Max Adet","Max Toplam",
                "Birim Fark","Fark%","Site#","Maliyet Detayi","URL"};
            Hdr(ws, 1, h, XLColor.FromHtml("#C00000"));
            for (int i = 0; i < ds.Count; i++)
            {
                var g = ds[i]; int r = i + 2;
                double pct = (g.MaxPrice > 0 && g.PriceDifference.HasValue) ? Math.Round((double)(g.PriceDifference.Value / g.MaxPrice!.Value) * 100, 1) : 0;
                ws.Cell(r, 1).Value = i + 1; ws.Cell(r, 2).Value = g.Category; ws.Cell(r, 3).Value = g.Capacity;
                ws.Cell(r, 4).Value = g.MinPrice.HasValue ? (double)g.MinPrice.Value : 0;
                ws.Cell(r, 5).Value = g.MinPriceStore;
                ws.Cell(r, 6).Value = g.MinPriceMinQty;
                ws.Cell(r, 7).Value = g.MinPriceTotalCost.HasValue ? (double)g.MinPriceTotalCost.Value : 0;
                ws.Cell(r, 8).Value = g.MaxPrice.HasValue ? (double)g.MaxPrice.Value : 0;
                ws.Cell(r, 9).Value = g.MaxPriceStore;
                ws.Cell(r, 10).Value = g.MaxPriceMinQty;
                ws.Cell(r, 11).Value = g.MaxPriceTotalCost.HasValue ? (double)g.MaxPriceTotalCost.Value : 0;
                ws.Cell(r, 12).Value = g.PriceDifference.HasValue ? (double)g.PriceDifference.Value : 0;
                ws.Cell(r, 13).Value = pct; ws.Cell(r, 14).Value = g.SiteCount;
                ws.Cell(r, 15).Value = g.SiteCostBreakdown;
                ws.Cell(r, 16).Value = g.MinPriceUrl;
                foreach (int c in new[] { 4, 7, 8, 11, 12 }) ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 13).Style.NumberFormat.Format = "0.0\"%\"";
                if (Uri.TryCreate(g.MinPriceUrl, UriKind.Absolute, out var u)) ws.Cell(r, 16).SetHyperlink(new XLHyperlink(u.ToString()));
                if (i < 10) RowBg(ws, r, h.Length, deal); else if (i % 2 == 1) RowBg(ws, r, h.Length, alt);
            }
            Tbl(ws, 1, ds.Count + 1, h.Length); ws.Columns().AdjustToContents();
            ws.Column(15).Width = 80; ws.Column(16).Width = 50;
        }

        // Teklif Gereken
        if (quote.Count > 0)
        {
            var ws = wb.Worksheets.Add("Teklif Gereken"); ws.SheetView.FreezeRows(1);
            var h = new[] { "Magaza", "Kategori", "Urun", "Min.Sip.", "URL", "Zaman" };
            Hdr(ws, 1, h, XLColor.FromHtml("#ED7D31"));
            for (int i = 0; i < quote.Count; i++)
            {
                var rw = quote[i]; int r = i + 2;
                ws.Cell(r, 1).Value = rw.Store; ws.Cell(r, 2).Value = rw.Category; ws.Cell(r, 3).Value = rw.ProductName;
                ws.Cell(r, 4).Value = rw.MinOrderQty; ws.Cell(r, 5).Value = rw.Url;
                ws.Cell(r, 6).Value = rw.Timestamp.ToString("dd.MM.yyyy HH:mm");
                if (Uri.TryCreate(rw.Url, UriKind.Absolute, out var u)) ws.Cell(r, 5).SetHyperlink(new XLHyperlink(u.ToString()));
                if (i % 2 == 1) RowBg(ws, r, h.Length, qBg);
            }
            Tbl(ws, 1, quote.Count + 1, h.Length); ws.Columns().AdjustToContents();
        }

        wb.SaveAs(filePath);
    }

    private static void Hdr(IXLWorksheet ws, int row, string[] h, XLColor bg)
    {
        for (int c = 0; c < h.Length; c++)
        {
            var cl = ws.Cell(row, c + 1); cl.Value = h[c]; cl.Style.Font.Bold = true;
            cl.Style.Font.FontColor = XLColor.White; cl.Style.Fill.BackgroundColor = bg;
            cl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }
    private static void RowBg(IXLWorksheet ws, int row, int cols, XLColor c) => ws.Range(row, 1, row, cols).Style.Fill.BackgroundColor = c;
    private static void Tbl(IXLWorksheet ws, int r1, int r2, int c2)
    {
        if (r2 <= r1) return;
        var rng = ws.Range(r1, 1, r2, c2); rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        rng.Style.Border.InsideBorder = XLBorderStyleValues.Hair; ws.Range(r1, 1, r1, c2).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
    }
}