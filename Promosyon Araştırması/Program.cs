using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Playwright;
using System.Diagnostics;

static void Log(string msg)
{
    Console.WriteLine(msg);
    Debug.WriteLine(msg);
}

static string NowStamp() => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

static string NormalizeUrl(string url)
{
    if (string.IsNullOrWhiteSpace(url)) return url;
    url = url.Trim();
    // fragment at
    var hash = url.IndexOf('#');
    if (hash >= 0) url = url[..hash];
    // trailing slash normalize (ama sadece root değilse)
    if (url.EndsWith("/") && url.Length > 8) url = url.TrimEnd('/');
    return url;
}

static bool LooksLikeFileDownload(Uri u)
{
    var p = u.AbsolutePath.ToLowerInvariant();
    return p.EndsWith(".pdf") || p.EndsWith(".zip") || p.EndsWith(".rar") ||
           p.EndsWith(".xlsx") || p.EndsWith(".xls") || p.EndsWith(".doc") || p.EndsWith(".docx");
}

static (decimal? price, string currency) ParsePrice(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return (null, "");
    // örn: "3.05 TL" / "3,05 ₺"
    var m = Regex.Match(text, @"(\d+[.,]?\d*)\s*(TL|₺|TRY|USD|EUR)?", RegexOptions.IgnoreCase);
    if (!m.Success) return (null, "");
    var num = m.Groups[1].Value.Replace(',', '.');
    if (!decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) return (null, "");
    var cur = (m.Groups[2].Value ?? "").ToUpperInvariant();
    if (cur == "TL" || cur == "₺") cur = "TRY";
    return (val, cur);
}

static async Task<string[]> ExtractSameHostLinksAsync(IPage page, Uri baseUri)
{
    // hızlı link çıkarma: tüm a[href] topla
    var hrefs = await page.EvaluateAsync<string[]>(
        @"() => Array.from(document.querySelectorAll('a[href]'))
                     .map(a => a.href)
                     .filter(Boolean)");

    var list = new List<string>(hrefs.Length);
    foreach (var h in hrefs)
    {
        if (!Uri.TryCreate(h, UriKind.Absolute, out var u)) continue;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) continue;
        if (!string.Equals(u.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

        var nu = NormalizeUrl(u.ToString());
        list.Add(nu);
    }
    return list.Distinct().ToArray();
}

static async Task<string> GetPageCategoryAsync(IPage page)
{
    // kategori için önce h1 dene
    var h1 = await page.QuerySelectorAsync("h1");
    if (h1 != null)
    {
        var t = (await h1.InnerTextAsync())?.Trim();
        if (!string.IsNullOrWhiteSpace(t)) return t!;
    }
    // fallback: title
    var title = (await page.TitleAsync())?.Trim();
    return title ?? "";
}

static async Task<List<ResultRow>> ExtractProductsAsync(IPage page, Uri pageUri, string seedUrl)
{
    var rows = new List<ResultRow>();
    var store = pageUri.Host;
    var category = await GetPageCategoryAsync(page);

    // 1) AksiyonPromosyon net template: .product-item
    var aksiyonCards = await page.QuerySelectorAllAsync(".product-item");
    if (aksiyonCards.Count > 0)
    {
        foreach (var card in aksiyonCards)
        {
            var nameEl = await card.QuerySelectorAsync("a.title");
            var catEl = await card.QuerySelectorAsync("a.cat");
            var priceEl = await card.QuerySelectorAsync(".get-price, .price, .product-price");

            var name = (nameEl != null ? await nameEl.InnerTextAsync() : "")?.Trim();
            var cat = (catEl != null ? await catEl.InnerTextAsync() : "")?.Trim();
            var priceText = (priceEl != null ? await priceEl.InnerTextAsync() : "")?.Trim();

            var href = nameEl != null ? await nameEl.GetAttributeAsync("href") : null;
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            var (p, ccy) = ParsePrice(priceText ?? "");

            rows.Add(new ResultRow
            {
                Store = store,
                SeedUrl = seedUrl,
                Url = abs,
                Category = string.IsNullOrWhiteSpace(cat) ? category : cat!,
                ProductName = name!,
                Price = p,
                Currency = ccy,
                QuantityPriceListJson = "",
                Timestamp = DateTimeOffset.Now,
                Error = ""
            });
        }
        return rows;
    }

    // 2) Generic fallback: yaygın kart seçicileri
    var cards = await page.QuerySelectorAllAsync(".product, .product-card, .product-item, li.product, .urun, .item");
    if (cards.Count == 0) return rows;

    foreach (var card in cards)
    {
        var a = await card.QuerySelectorAsync("a[href]");
        if (a == null) continue;

        var href = await a.GetAttributeAsync("href");
        var name = (await a.InnerTextAsync())?.Trim();

        if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

        var priceText = (await card.InnerTextAsync()) ?? "";
        var (p, ccy) = ParsePrice(priceText);

        var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;

        rows.Add(new ResultRow
        {
            Store = store,
            SeedUrl = seedUrl,
            Url = abs,
            Category = category,
            ProductName = name!,
            Price = p,
            Currency = ccy,
            QuantityPriceListJson = "",
            Timestamp = DateTimeOffset.Now,
            Error = ""
        });
    }

    return rows;
}

// ===================== RUN =====================

var projectRoot = Directory.GetCurrentDirectory();
var outDir = Path.Combine(projectRoot, "out", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(outDir);

var logPath = Path.Combine(outDir, "run.log");
void LogFile(string msg)
{
    var line = $"[{NowStamp()}] {msg}";
    Log(line);
    File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
}

var urlsPath = Path.Combine(AppContext.BaseDirectory, "urls.txt"); // Copy always dediğin için bin'e gelir
if (!File.Exists(urlsPath))
{
    LogFile($"[ERR] urls.txt bulunamadı: {urlsPath}");
    return;
}

var seeds = File.ReadAllLines(urlsPath)
    .Select(l => l.Trim())
    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
    .Select(NormalizeUrl)
    .Distinct()
    .ToList();

LogFile($"OUT: {outDir}");
LogFile($"Seed count: {seeds.Count}");

var results = new List<ResultRow>();
var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var skipped = new List<string>();
var failed = new List<string>();

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Headless = true
});

var context = await browser.NewContextAsync(new()
{
    IgnoreHTTPSErrors = true
});

// (opsiyonel) ağır kaynakları kes: img/font/media
await context.RouteAsync("**/*", async route =>
{
    var rt = route.Request.ResourceType;
    if (rt is "image" or "font" or "media")
        await route.AbortAsync();
    else
        await route.ContinueAsync();
});

var page = await context.NewPageAsync();
page.SetDefaultNavigationTimeout(120000);
page.SetDefaultTimeout(15000);

// BFS queue
var q = new Queue<(string seed, string url)>();
foreach (var s in seeds) q.Enqueue((s, s));

const int MAX_PAGES_TOTAL = 400; // şimdilik güvenlik limiti
int processed = 0;

while (q.Count > 0 && processed < MAX_PAGES_TOTAL)
{
    var (seed, url) = q.Dequeue();
    url = NormalizeUrl(url);

    if (visited.Contains(url)) continue;
    visited.Add(url);
    processed++;

    if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
    {
        skipped.Add(url);
        LogFile($"[SKIP] bad url: {url}");
        continue;
    }

    if (LooksLikeFileDownload(u))
    {
        skipped.Add(url);
        LogFile($"[SKIP] download-like: {url}");
        continue;
    }

    LogFile($"[OPEN] {url}");

    try
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 120000 });
        await page.WaitForTimeoutAsync(500);

        var extracted = await ExtractProductsAsync(page, u, seed);

        if (extracted.Count > 0)
        {
            results.AddRange(extracted);
            LogFile($"[OK] {url} -> {extracted.Count} ürün");
        }
        else
        {
            LogFile($"[SKIP] {url} -> ürün bulunamadı");
        }

        // aynı host linkleri kuyruğa ekle (seed bazlı crawl)
        var links = await ExtractSameHostLinksAsync(page, u);
        foreach (var link in links)
        {
            if (!visited.Contains(link))
                q.Enqueue((seed, link));
        }
    }
    catch (TimeoutException ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
        LogFile($"[ERR] {url} -> TIMEOUT: {ex.Message}");
    }
    catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase))
    {
        skipped.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, "Download is starting"));
        LogFile($"[SKIP] {url} -> Download is starting");
    }
    catch (PlaywrightException ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.Message));
        LogFile($"[ERR] {url} -> {ex.Message}");
    }
    catch (Exception ex)
    {
        failed.Add(url);
        results.Add(ResultRow.ErrorRow(u.Host, seed, url, ex.ToString()));
        LogFile($"[ERR] {url} -> {ex.GetType().Name}: {ex.Message}");
    }
}

// dosyaları yaz
File.WriteAllLines(Path.Combine(outDir, "visited_urls.txt"), visited.OrderBy(x => x), Encoding.UTF8);
File.WriteAllLines(Path.Combine(outDir, "skipped_urls.txt"), skipped.Distinct().OrderBy(x => x), Encoding.UTF8);
File.WriteAllLines(Path.Combine(outDir, "failed_urls.txt"), failed.Distinct().OrderBy(x => x), Encoding.UTF8);

var csvPath = Path.Combine(outDir, "results.csv");
var cfg = new CsvConfiguration(CultureInfo.GetCultureInfo("tr-TR"))
{
    Delimiter = ";",
    HasHeaderRecord = true
};

using (var sw = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
using (var csv = new CsvWriter(sw, cfg))
{
    csv.WriteHeader<ResultRow>();
    csv.NextRecord();
    foreach (var r in results)
    {
        csv.WriteRecord(r);
        csv.NextRecord();
    }
}

LogFile($"Bitti. Visited={visited.Count}, ResultsRows={results.Count}");
LogFile($"CSV: {csvPath}");

public class ResultRow
{
    public string Store { get; set; } = "";
    public string SeedUrl { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "";
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
