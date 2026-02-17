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
    var hash = url.IndexOf('#');
    if (hash >= 0) url = url[..hash];
    if (url.EndsWith("/") && url.Length > 8) url = url.TrimEnd('/');
    return url;
}

static bool LooksLikeFileDownload(Uri u)
{
    var p = u.AbsolutePath.ToLowerInvariant();
    return p.EndsWith(".pdf") || p.EndsWith(".zip") || p.EndsWith(".rar") ||
           p.EndsWith(".xlsx") || p.EndsWith(".xls") || p.EndsWith(".doc") || p.EndsWith(".docx");
}

static bool IsNavigationLink(string url, string text)
{
    var lowerUrl = url.ToLowerInvariant();
    var lowerText = text.ToLowerInvariant();

    var navKeywords = new[] {
        "hakkımızda", "iletişim", "blog", "hesap", "account", "giriş", "login",
        "kayıt", "register", "sepet", "cart", "favoriler", "wishlist",
        "yardım", "help", "sss", "faq", "nasıl", "how", "kargo", "shipping",
        "iade", "return", "kullanım", "terms", "gizlilik", "privacy",
        "çerez", "cookie", "kvkk", "sözleşme", "agreement", "kampanya",
        "teklif iste", "numune", "tedarikçi", "kariyer", "career"
    };

    if (navKeywords.Any(k => lowerUrl.Contains(k) || lowerText.Contains(k)))
    {
        if (!Regex.IsMatch(text, @"\d{4,}") && text.Length < 60)
        {
            return true;
        }
    }

    return false;
}

static bool IsCategoryLink(string url, string text, string store)
{
    var lowerUrl = url.ToLowerInvariant();
    var lowerText = text.ToLowerInvariant();

    // Kategori kelimeleri
    var categoryKeywords = new[] {
        "promosyon", "urun", "kategori", "tisort", "kalem", "ajanda",
        "termos", "kupa", "defter", "canta", "sapka", "powerbank",
        "usb", "mouse", "kartvizit", "takvim", "ofis", "kirtasiye"
    };

    // URL'de veya text'te kategori kelimesi var mı?
    bool hasKeyword = categoryKeywords.Any(k => lowerUrl.Contains(k) || lowerText.Contains(k));

    // Navigasyon linki değilse ve kategori kelimesi varsa = KATEGORİ!
    if (hasKeyword && !IsNavigationLink(url, text))
    {
        // Çok uzun text = açıklama, kategori değil
        if (text.Length > 100) return false;

        return true;
    }

    return false;
}

static async Task<List<string>> FindCategoryLinksAsync(IPage page, Uri baseUri, string store)
{
    var links = new List<string>();

    try
    {
        // Tüm linkleri al
        var allLinks = await page.QuerySelectorAllAsync("a[href]");

        foreach (var linkEl in allLinks.Take(200)) // Max 200 link kontrol et
        {
            var href = await linkEl.GetAttributeAsync("href");
            var text = (await linkEl.InnerTextAsync())?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(href)) continue;

            // Absolute URL yap
            if (!Uri.TryCreate(baseUri, href, out var absUri)) continue;

            // Aynı host'ta mı?
            if (!string.Equals(absUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

            // Kategori linki mi?
            if (IsCategoryLink(absUri.ToString(), text, store))
            {
                links.Add(NormalizeUrl(absUri.ToString()));
            }
        }
    }
    catch (Exception ex)
    {
        Log($"[WARN] Kategori bulunamadı: {ex.Message}");
    }

    return links.Distinct().Take(30).ToList(); // Max 30 kategori
}

static (decimal? price, string currency, bool requiresQuote, bool hasKdv, bool isPriceValid) ParsePrice(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return (null, "", false, false, false);

    var quoteKeywords = new[] {
        "teklif", "iletişim", "fiyat için", "bilgi için",
        "sipariş", "arayın", "sorun", "sor", "contact", "fiyat alınız"
    };

    var lowerText = text.ToLowerInvariant();
    bool requiresQuote = quoteKeywords.Any(k => lowerText.Contains(k));
    bool hasKdv = lowerText.Contains("+kdv") || lowerText.Contains("+ kdv") || lowerText.Contains("+kDv");

    var measurementUnits = new[] { " lt", " ml", " kg", " gr", " cm", " mm", " adet" };
    if (measurementUnits.Any(u => lowerText.Contains(u)))
    {
        if (!lowerText.Contains("tl") && !lowerText.Contains("₺"))
        {
            return (null, "", requiresQuote, hasKdv, false);
        }
    }

    var patterns = new[]
    {
        @"(\d{1,3}(?:\.\d{3})*,\d{2})\s*(TL|₺|TRY|USD|EUR)?",
        @"(\d{1,3}(?:,\d{3})*\.\d{2})\s*(TL|₺|TRY|USD|EUR)?",
        @"(\d+,\d{2})\s*(TL|₺|TRY|USD|EUR)?",
        @"(\d+\.\d{2})\s*(TL|₺|TRY|USD|EUR)?",
        @"(\d+)\s*(TL|₺|TRY|USD|EUR)"
    };

    foreach (var pattern in patterns)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var numStr = m.Groups[1].Value;

            if (numStr.Contains('.') && numStr.Contains(','))
            {
                if (numStr.LastIndexOf(',') > numStr.LastIndexOf('.'))
                {
                    numStr = numStr.Replace(".", "").Replace(',', '.');
                }
                else
                {
                    numStr = numStr.Replace(",", "");
                }
            }
            else if (numStr.Contains(','))
            {
                numStr = numStr.Replace(',', '.');
            }

            if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                var cur = (m.Groups[2].Value ?? "").ToUpperInvariant();
                if (cur == "TL" || cur == "₺") cur = "TRY";
                if (string.IsNullOrEmpty(cur)) cur = "TRY";

                bool isValid = true;

                if (val < 1.0m) isValid = false;
                if (val > 100000m) isValid = false;

                return (val, cur, requiresQuote, hasKdv, isValid);
            }
        }
    }

    return (null, "", requiresQuote, hasKdv, false);
}

static string CleanProductName(string name)
{
    if (string.IsNullOrWhiteSpace(name)) return "";

    var lines = name.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

    if (lines.Count == 0) return "";

    var productName = lines[0];

    productName = Regex.Replace(productName, @"\d+\s*adet", "", RegexOptions.IgnoreCase).Trim();
    productName = Regex.Replace(productName, @"\d+[.,]\d+\s*(TL|₺|TRY)", "", RegexOptions.IgnoreCase).Trim();
    productName = Regex.Replace(productName, @"\+?KDV", "", RegexOptions.IgnoreCase).Trim();
    productName = Regex.Replace(productName, @"\(\d+\)", "").Trim();

    return productName;
}

static async Task<string[]> ExtractSameHostLinksAsync(IPage page, Uri baseUri)
{
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
    var h1 = await page.QuerySelectorAsync("h1");
    if (h1 != null)
    {
        var t = (await h1.InnerTextAsync())?.Trim();
        if (!string.IsNullOrWhiteSpace(t)) return t!;
    }
    var title = (await page.TitleAsync())?.Trim();
    return title ?? "";
}

static ResultRow CreateRow(string store, string seedUrl, string url, string category,
    string productName, decimal? price, string currency, bool requiresQuote, bool hasKDV)
{
    return new ResultRow
    {
        Store = store,
        SeedUrl = seedUrl,
        Url = url,
        Category = category,
        ProductName = CleanProductName(productName),
        Price = price,
        Currency = currency,
        RequiresQuote = requiresQuote,
        HasKDV = hasKDV,
        QuantityPriceListJson = "",
        Timestamp = DateTimeOffset.Now,
        Error = ""
    };
}

static async Task<List<ResultRow>> ExtractProductsAsync(IPage page, Uri pageUri, string seedUrl)
{
    var rows = new List<ResultRow>();
    var store = pageUri.Host;
    var category = await GetPageCategoryAsync(page);

    // 1. BIDOLUBASKI.COM
    if (store.Contains("bidolubaski"))
    {
        try
        {
            await page.WaitForSelectorAsync(".flex.flex-col, .swiper-slide", new() { Timeout = 5000 });
        }
        catch { }

        var cards = await page.QuerySelectorAllAsync(".flex.flex-col, .swiper-slide");

        foreach (var card in cards)
        {
            var nameEl = await card.QuerySelectorAsync("a[href*='/']");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;
            if (IsNavigationLink(href, name)) continue;
            if (name.Length < 5) continue;

            var priceEl = await card.QuerySelectorAsync("span.font-semibold, .text-lg");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() : "";

            var (p, ccy, quote, kdv, valid) = ParsePrice(priceText ?? "");
            if (!valid && p.HasValue) continue;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
            }
        }
        return rows;
    }

    // 2. PROMOZONE.COM.TR
    if (store.Contains("promozone"))
    {
        try
        {
            await page.WaitForSelectorAsync("div[class*='product-item'], .swiper-slide", new() { Timeout = 5000 });
        }
        catch { }

        var items = await page.QuerySelectorAllAsync("div[class*='product-item'], .swiper-slide");

        foreach (var item in items)
        {
            var nameEl = await item.QuerySelectorAsync("a[href*='/urun/']");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;
            if (name.Length < 5) continue;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(CreateRow(store, seedUrl, abs, category, name, null, "TRY", true, false));
        }
        return rows;
    }

    // 3. TURKUAZPROMOSYON.COM.TR
    if (store.Contains("turkuazpromosyon"))
    {
        try
        {
            await page.WaitForSelectorAsync(".left, .product-block", new() { Timeout = 5000 });
        }
        catch { }

        var items = await page.QuerySelectorAllAsync(".left, .product-block");

        foreach (var item in items)
        {
            var nameEl = await item.QuerySelectorAsync("h3.name a, .name a");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await item.QuerySelectorAsync(".price-gruop, .price .special-price");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() : "";

            var (p, ccy, quote, kdv, valid) = ParsePrice(priceText ?? "");
            if (!valid && p.HasValue) continue;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
            }
        }
        return rows;
    }

    // 4. BATIPROMOSYON.COM.TR
    if (store.Contains("batipromosyon"))
    {
        try
        {
            await page.WaitForSelectorAsync(".product-card", new() { Timeout = 5000 });
        }
        catch { }

        var cards = await page.QuerySelectorAllAsync(".product-card");

        foreach (var card in cards)
        {
            var nameEl = await card.QuerySelectorAsync("a[href*='/urun']");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await card.QuerySelectorAsync(".product-card__price--new, .product-card__price");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() : "";

            var (p, ccy, quote, kdv, valid) = ParsePrice(priceText ?? "");
            if (!valid && p.HasValue) continue;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
            }
        }
        return rows;
    }

    // 5. TEKINOZALIT.COM
    if (store.Contains("tekinozalit"))
    {
        try
        {
            await page.WaitForSelectorAsync(".relative.flex.flex-col", new() { Timeout = 5000 });
        }
        catch { }

        var cards = await page.QuerySelectorAllAsync(".relative.flex.flex-col");

        foreach (var card in cards)
        {
            var linkEl = await card.QuerySelectorAsync("a[href*='/']");
            if (linkEl == null) continue;

            var nameEl = await card.QuerySelectorAsync(".text-brand-pink-02, .line-clamp-1, span");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await linkEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await card.QuerySelectorAsync("span.font-medium, .price");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() : "";

            var (p, ccy, quote, kdv, valid) = ParsePrice(priceText ?? "");
            if (!valid && p.HasValue) continue;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
            }
        }
        return rows;
    }

    // 6. PROMOSYONIK.COM
    if (store.Contains("promosyonik"))
    {
        try
        {
            await page.WaitForSelectorAsync(".product-card-inner", new() { Timeout = 5000 });
        }
        catch { }

        var cards = await page.QuerySelectorAllAsync(".product-card-inner");

        foreach (var card in cards)
        {
            var nameEl = await card.QuerySelectorAsync(".product-card-text a, a[title]");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(CreateRow(store, seedUrl, abs, category, name, null, "TRY", true, false));
        }
        return rows;
    }

    // 7. AKSIYONPROMOSYON.COM
    if (store.Contains("aksiyonpromosyon"))
    {
        try
        {
            await page.WaitForSelectorAsync(".product-item", new() { Timeout = 5000 });
        }
        catch { }

        var items = await page.QuerySelectorAllAsync(".product-item");

        foreach (var item in items)
        {
            var nameEl = await item.QuerySelectorAsync("a.cat, .title a");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await item.QuerySelectorAsync(".get-price.price, .price.net");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() : "";

            var (p, ccy, quote, kdv, valid) = ParsePrice(priceText ?? "");
            if (!valid && p.HasValue) continue;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
            }
        }
        return rows;
    }

    // 8. ILPEN.COM.TR
    if (store.Contains("ilpen"))
    {
        try
        {
            await page.WaitForSelectorAsync(".laberProductGrid .item", new() { Timeout = 5000 });
        }
        catch { }

        var items = await page.QuerySelectorAllAsync(".laberProductGrid .item");

        foreach (var item in items)
        {
            var nameEl = await item.QuerySelectorAsync("h2.productName a, .product-name a");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(CreateRow(store, seedUrl, abs, category, name, null, "TRY", true, false));
        }
        return rows;
    }

    return rows;
}

// ===================== MAIN =====================

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

var urlsPath = Path.Combine(AppContext.BaseDirectory, "urls.txt");
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

await context.RouteAsync("**/*", async route =>
{
    var rt = route.Request.ResourceType;
    if (rt is "image" or "font" or "media" or "stylesheet")
        await route.AbortAsync();
    else
        await route.ContinueAsync();
});

var page = await context.NewPageAsync();
page.SetDefaultNavigationTimeout(90000);
page.SetDefaultTimeout(10000);

var q = new Queue<(string seed, string url)>();
foreach (var s in seeds) q.Enqueue((s, s));

const int MAX_PAGES_TOTAL = 400;
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
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90000 });
        await page.WaitForTimeoutAsync(500);

        var extracted = await ExtractProductsAsync(page, u, seed);

        if (extracted.Count > 0)
        {
            results.AddRange(extracted);
            LogFile($"[OK] {url} -> {extracted.Count} ürün");
        }
        else
        {
            // ÜRÜN BULUNAMADI - KATEGORİ LİNKLERİNİ BUL!
            LogFile($"[INFO] {url} -> ürün yok, kategoriler aranıyor...");

            var categoryLinks = await FindCategoryLinksAsync(page, u, u.Host);

            if (categoryLinks.Any())
            {
                LogFile($"[INFO] {categoryLinks.Count} kategori bulundu, queue'ya ekleniyor");
                foreach (var catLink in categoryLinks)
                {
                    if (!visited.Contains(catLink))
                        q.Enqueue((seed, catLink));
                }
            }
            else
            {
                LogFile($"[SKIP] {url} -> ne ürün ne de kategori bulunamadı");
            }
        }

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

File.WriteAllLines(Path.Combine(outDir, "visited_urls.txt"), visited.OrderBy(x => x), Encoding.UTF8);
File.WriteAllLines(Path.Combine(outDir, "skipped_urls.txt"), skipped.Distinct().OrderBy(x => x), Encoding.UTF8);
File.WriteAllLines(Path.Combine(outDir, "failed_urls.txt"), failed.Distinct().OrderBy(x => x), Encoding.UTF8);

var cfg = new CsvConfiguration(CultureInfo.GetCultureInfo("tr-TR"))
{
    Delimiter = ";",
    HasHeaderRecord = true
};

var csvPath = Path.Combine(outDir, "results.csv");
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

LogFile("Duplicate kayıtlar temizleniyor...");
var validProducts = results
    .Where(r => string.IsNullOrEmpty(r.Error))
    .Where(r => !string.IsNullOrWhiteSpace(r.ProductName))
    .Where(r => r.ProductName.Length >= 5)
    .GroupBy(r => r.Url)
    .Select(g => g.First())
    .ToList();

LogFile($"Temizleme: {results.Count} → {validProducts.Count} (Duplicate: {results.Count - validProducts.Count})");

var validPath = Path.Combine(outDir, "products_valid.csv");
using (var sw = new StreamWriter(validPath, false, new UTF8Encoding(true)))
using (var csv = new CsvWriter(sw, cfg))
{
    csv.WriteHeader<ResultRow>();
    csv.NextRecord();
    foreach (var r in validProducts)
    {
        csv.WriteRecord(r);
        csv.NextRecord();
    }
}

var quoteProducts = validProducts.Where(r => r.RequiresQuote).ToList();
if (quoteProducts.Any())
{
    var quotePath = Path.Combine(outDir, "requires_quote.csv");
    using var sw = new StreamWriter(quotePath, false, new UTF8Encoding(true));
    using var csv = new CsvWriter(sw, cfg);
    csv.WriteHeader<ResultRow>();
    csv.NextRecord();
    foreach (var r in quoteProducts)
    {
        csv.WriteRecord(r);
        csv.NextRecord();
    }
    LogFile($"'Teklif Alın' ürünleri: {quoteProducts.Count} adet");
}

var pricedProducts = validProducts.Where(r => r.Price.HasValue && r.Price > 0).ToList();
var pricedPath = Path.Combine(outDir, "products_priced.csv");
using (var sw = new StreamWriter(pricedPath, false, new UTF8Encoding(true)))
using (var csv = new CsvWriter(sw, cfg))
{
    csv.WriteHeader<ResultRow>();
    csv.NextRecord();
    foreach (var r in pricedProducts)
    {
        csv.WriteRecord(r);
        csv.NextRecord();
    }
}

if (pricedProducts.Count > 0)
{
    LogFile($"🧠 AKILLI karşılaştırma yapılıyor...");

    var smartGroups = SmartProductMatcher.GroupSimilarProducts(pricedProducts);

    var smartPath = Path.Combine(outDir, "smart_comparison.csv");
    using var sw = new StreamWriter(smartPath, false, new UTF8Encoding(true));
    using var csv = new CsvWriter(sw, cfg);
    csv.WriteHeader<SmartProductGroup>();
    csv.NextRecord();
    foreach (var g in smartGroups)
    {
        csv.WriteRecord(g);
        csv.NextRecord();
    }

    LogFile($"✅ Akıllı karşılaştırma: {smartGroups.Count} grup bulundu");
    LogFile($"✅ 2+ sitede: {smartGroups.Count(g => g.SiteCount >= 2)} grup");

    var bestDeals = smartGroups
        .Where(g => g.SiteCount >= 2)
        .OrderByDescending(g => g.PriceDifference ?? 0)
        .Take(50)
        .ToList();

    if (bestDeals.Any())
    {
        var dealsPath = Path.Combine(outDir, "best_deals.csv");
        using var sw2 = new StreamWriter(dealsPath, false, new UTF8Encoding(true));
        using var csv2 = new CsvWriter(sw2, cfg);
        csv2.WriteHeader<SmartProductGroup>();
        csv2.NextRecord();
        foreach (var d in bestDeals)
        {
            csv2.WriteRecord(d);
            csv2.NextRecord();
        }
        LogFile($"⭐ En iyi fırsatlar: {bestDeals.Count} ürün");
    }
}

LogFile($"===== ÖZET =====");
LogFile($"Toplam sayfa: {visited.Count}");
LogFile($"Ham kayıt: {results.Count}");
LogFile($"Duplicate temizlendi: {results.Count - validProducts.Count}");
LogFile($"Geçerli ürün: {validProducts.Count}");
LogFile($"Fiyatlı ürün: {pricedProducts.Count}");
LogFile($"Teklif gereken: {quoteProducts.Count}");
LogFile($"Başarısız: {failed.Count}");
LogFile($"CSV: {csvPath}");

// ===================== ANALİZ RAPORU =====================

LogFile("");
LogFile("📊 ===== DETAYLI ANALİZ RAPORU =====");

var siteStats = validProducts
    .GroupBy(p => p.Store)
    .Select(g => new
    {
        Store = g.Key,
        TotalProducts = g.Count(),
        PricedProducts = g.Count(p => p.Price.HasValue),
        QuoteProducts = g.Count(p => p.RequiresQuote),
        AvgPrice = g.Where(p => p.Price.HasValue).Any()
            ? g.Where(p => p.Price.HasValue).Average(p => p.Price!.Value)
            : 0
    })
    .OrderByDescending(s => s.TotalProducts)
    .ToList();

LogFile("");
LogFile("🏪 SİTE BAZINDA DAĞILIM:");
foreach (var stat in siteStats)
{
    LogFile($"  • {stat.Store}:");
    LogFile($"    - Toplam: {stat.TotalProducts} ürün");
    LogFile($"    - Fiyatlı: {stat.PricedProducts} ürün");
    LogFile($"    - Teklif: {stat.QuoteProducts} ürün");
    if (stat.AvgPrice > 0)
        LogFile($"    - Ort. Fiyat: {stat.AvgPrice:F2} TL");
}

var categoryStats = validProducts
    .Where(p => !string.IsNullOrWhiteSpace(p.Category))
    .GroupBy(p => p.Category)
    .Select(g => new
    {
        Category = g.Key,
        Count = g.Count(),
        AvgPrice = g.Where(p => p.Price.HasValue).Any()
            ? g.Where(p => p.Price.HasValue).Average(p => p.Price!.Value)
            : 0
    })
    .OrderByDescending(c => c.Count)
    .Take(10)
    .ToList();

LogFile("");
LogFile("📦 KATEGORİ DAĞILIMI (TOP 10):");
foreach (var cat in categoryStats)
{
    LogFile($"  • {cat.Category}: {cat.Count} ürün" +
        (cat.AvgPrice > 0 ? $" (Ort: {cat.AvgPrice:F2} TL)" : ""));
}

if (pricedProducts.Any())
{
    LogFile("");
    LogFile("💰 FİYAT İSTATİSTİKLERİ:");
    var minPrice = pricedProducts.Min(p => p.Price!.Value);
    var maxPrice = pricedProducts.Max(p => p.Price!.Value);
    var avgPrice = pricedProducts.Average(p => p.Price!.Value);

    LogFile($"  • En Düşük: {minPrice:F2} TL");
    LogFile($"  • En Yüksek: {maxPrice:F2} TL");
    LogFile($"  • Ortalama: {avgPrice:F2} TL");

    var ranges = new[]
    {
        ("0-50 TL", pricedProducts.Count(p => p.Price < 50)),
        ("50-100 TL", pricedProducts.Count(p => p.Price >= 50 && p.Price < 100)),
        ("100-250 TL", pricedProducts.Count(p => p.Price >= 100 && p.Price < 250)),
        ("250-500 TL", pricedProducts.Count(p => p.Price >= 250 && p.Price < 500)),
        ("500+ TL", pricedProducts.Count(p => p.Price >= 500))
    };

    LogFile("");
    LogFile("📊 FİYAT ARALIĞI DAĞILIMI:");
    foreach (var (range, count) in ranges)
    {
        if (count > 0)
        {
            var percentage = (count * 100.0 / pricedProducts.Count);
            LogFile($"  • {range}: {count} ürün ({percentage:F1}%)");
        }
    }

    LogFile("");
    LogFile("🏆 EN UCUZ 20 ÜRÜN:");
    var cheapest = pricedProducts.OrderBy(p => p.Price).Take(20).ToList();
    for (int i = 0; i < cheapest.Count; i++)
    {
        var p = cheapest[i];
        var name = p.ProductName.Length > 50
            ? p.ProductName.Substring(0, 47) + "..."
            : p.ProductName;
        LogFile($"  {i + 1}. {name} - {p.Price:F2} TL ({p.Store})");
    }

    LogFile("");
    LogFile("💎 EN PAHALI 20 ÜRÜN:");
    var expensive = pricedProducts.OrderByDescending(p => p.Price).Take(20).ToList();
    for (int i = 0; i < expensive.Count; i++)
    {
        var p = expensive[i];
        var name = p.ProductName.Length > 50
            ? p.ProductName.Substring(0, 47) + "..."
            : p.ProductName;
        LogFile($"  {i + 1}. {name} - {p.Price:F2} TL ({p.Store})");
    }
}

var promozoneProducts = validProducts.Where(p => p.Store.Contains("promozone")).ToList();
if (promozoneProducts.Any())
{
    LogFile("");
    LogFile("🏪 PROMOZONE ÖZEL ANALİZ:");
    LogFile($"  • Toplam Ürün: {promozoneProducts.Count}");
    LogFile($"  • Teklif Gereken: {promozoneProducts.Count(p => p.RequiresQuote)}");

    var promozoneCategories = promozoneProducts
        .Where(p => !string.IsNullOrWhiteSpace(p.Category))
        .GroupBy(p => p.Category)
        .OrderByDescending(g => g.Count())
        .Take(10)
        .ToList();

    LogFile("  • En Çok Ürün Olan Kategoriler:");
    foreach (var cat in promozoneCategories)
    {
        LogFile($"    - {cat.Key}: {cat.Count()} ürün");
    }
}

LogFile("");
LogFile("📊 ===== ANALİZ RAPORU SONU =====");

var analysisPath = Path.Combine(outDir, "analysis_report.txt");
var reportLines = new List<string>
{
    "=".PadRight(60, '='),
    "PROMOSYON ÜRÜN KARŞILAŞTIRMA ANALİZ RAPORU",
    $"Tarih: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
    "=".PadRight(60, '='),
    "",
    "GENEL İSTATİSTİKLER:",
    $"  Toplam Sayfa Tarandı: {visited.Count}",
    $"  Ham Kayıt: {results.Count}",
    $"  Geçerli Ürün: {validProducts.Count}",
    $"  Fiyatlı Ürün: {pricedProducts.Count}",
    $"  Teklif Gereken: {quoteProducts.Count}",
    ""
};

reportLines.Add("SİTE BAZINDA DAĞILIM:");
foreach (var stat in siteStats)
{
    reportLines.Add($"  {stat.Store}:");
    reportLines.Add($"    - Toplam: {stat.TotalProducts} ürün");
    reportLines.Add($"    - Fiyatlı: {stat.PricedProducts} ürün");
    reportLines.Add($"    - Teklif: {stat.QuoteProducts} ürün");
    if (stat.AvgPrice > 0)
        reportLines.Add($"    - Ortalama Fiyat: {stat.AvgPrice:F2} TL");
    reportLines.Add("");
}

File.WriteAllLines(analysisPath, reportLines, Encoding.UTF8);
LogFile($"📄 Analiz raporu kaydedildi: {analysisPath}");

public static class SmartProductMatcher
{
    public static ProductFeatures ExtractFeatures(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return new ProductFeatures();

        var lower = productName.ToLowerInvariant();
        var features = new ProductFeatures { OriginalName = productName };

        var capacityMatch = Regex.Match(lower, @"(\d+)[.,]?(\d*)\s*(mah|gb|mb|ml|lt|kg|gr)");
        if (capacityMatch.Success)
        {
            var number = capacityMatch.Groups[1].Value + (capacityMatch.Groups[2].Success ? capacityMatch.Groups[2].Value : "");
            var unit = capacityMatch.Groups[3].Value;
            features.Capacity = $"{number}{unit}";
        }

        var categories = new Dictionary<string, string[]>
        {
            ["powerbank"] = new[] { "powerbank", "sarj cihazi", "mobil sarj" },
            ["usb"] = new[] { "usb bellek", "flash disk", "usb disk" },
            ["kalem"] = new[] { "kalem", "tukenmez", "kursun kalem", "roller" },
            ["kupa"] = new[] { "kupa", "bardak", "fincan", "mug" },
            ["defter"] = new[] { "defter", "ajanda", "notebook" },
            ["canta"] = new[] { "canta", "torba", "poset", "sirt canta" },
            ["termos"] = new[] { "termos", "matara", "suluk" },
            ["sapka"] = new[] { "sapka", "bone", "bere", "tisort" },
            ["takvim"] = new[] { "takvim", "calendar" },
            ["mousepad"] = new[] { "mouse pad", "mousepad", "fare alti" }
        };

        foreach (var cat in categories)
        {
            if (cat.Value.Any(keyword => lower.Contains(keyword)))
            {
                features.Category = cat.Key;
                break;
            }
        }

        var propertyKeywords = new Dictionary<string, string[]>
        {
            ["wireless"] = new[] { "wireless", "kablosuz", "wi-fi" },
            ["magsafe"] = new[] { "magsafe", "magnetic" },
            ["led"] = new[] { "led", "isikli", "light" },
            ["solar"] = new[] { "solar", "gunes enerjili" },
            ["dijital"] = new[] { "dijital", "digital", "lcd" },
            ["hizli_sarj"] = new[] { "hizli sarj", "fast charge", "quick charge", "pd" },
            ["dahili_kablo"] = new[] { "dahili kablo", "built-in cable" },
            ["ekolojik"] = new[] { "ekolojik", "eco", "bambu", "ahsap" }
        };

        foreach (var prop in propertyKeywords)
        {
            if (prop.Value.Any(keyword => lower.Contains(keyword)))
            {
                features.Properties.Add(prop.Key);
            }
        }

        var brands = new[] { "stanley", "eccotech", "samsung", "apple", "xiaomi", "anker" };
        foreach (var brand in brands)
        {
            if (lower.Contains(brand))
            {
                features.Brand = brand;
                break;
            }
        }

        var colors = new[] { "siyah", "beyaz", "kirmizi", "mavi", "yesil", "sari", "gri", "pembe", "turuncu" };
        foreach (var color in colors)
        {
            if (lower.Contains(color))
            {
                features.Color = color;
                break;
            }
        }

        return features;
    }

    public static bool AreSimilar(ProductFeatures f1, ProductFeatures f2, double threshold = 0.6)
    {
        if (f1.Category != f2.Category) return false;

        double score = 0;
        int totalChecks = 0;

        if (!string.IsNullOrEmpty(f1.Capacity) && !string.IsNullOrEmpty(f2.Capacity))
        {
            totalChecks += 2;
            if (f1.Capacity == f2.Capacity)
                score += 2;
        }

        if (!string.IsNullOrEmpty(f1.Brand) || !string.IsNullOrEmpty(f2.Brand))
        {
            totalChecks++;
            if (f1.Brand == f2.Brand)
                score++;
        }

        if (f1.Properties.Any() || f2.Properties.Any())
        {
            var commonProps = f1.Properties.Intersect(f2.Properties).Count();
            var totalProps = f1.Properties.Union(f2.Properties).Count();

            if (totalProps > 0)
            {
                totalChecks++;
                score += (double)commonProps / totalProps;
            }
        }

        if (!string.IsNullOrEmpty(f1.Color) && !string.IsNullOrEmpty(f2.Color))
        {
            totalChecks++;
            if (f1.Color == f2.Color)
                score += 0.5;
        }

        if (totalChecks == 0) return false;

        double similarity = score / totalChecks;
        return similarity >= threshold;
    }

    public static List<SmartProductGroup> GroupSimilarProducts(List<ResultRow> products)
    {
        var groups = new List<SmartProductGroup>();
        var processed = new HashSet<string>();

        foreach (var product in products)
        {
            if (processed.Contains(product.Url)) continue;

            var features1 = ExtractFeatures(product.ProductName);

            var group = new SmartProductGroup
            {
                Category = features1.Category,
                Capacity = features1.Capacity,
                KeyFeatures = string.Join(", ", features1.Properties),
                Products = new List<ResultRow> { product }
            };

            processed.Add(product.Url);

            foreach (var other in products)
            {
                if (processed.Contains(other.Url)) continue;

                var features2 = ExtractFeatures(other.ProductName);

                if (AreSimilar(features1, features2))
                {
                    group.Products.Add(other);
                    processed.Add(other.Url);
                }
            }

            if (group.Products.Count >= 1)
            {
                group.SiteCount = group.Products.Select(p => p.Store).Distinct().Count();
                group.ProductCount = group.Products.Count;

                var priced = group.Products.Where(p => p.Price.HasValue).ToList();
                if (priced.Any())
                {
                    var minProduct = priced.OrderBy(p => p.Price).First();
                    var maxProduct = priced.OrderByDescending(p => p.Price).First();

                    group.MinPrice = minProduct.Price;
                    group.MinPriceStore = minProduct.Store;
                    group.MinPriceUrl = minProduct.Url;
                    group.MaxPrice = maxProduct.Price;
                    group.MaxPriceStore = maxProduct.Store;
                    group.AvgPrice = priced.Average(p => p.Price);
                    group.PriceDifference = group.MaxPrice - group.MinPrice;
                }

                group.AllProductNames = string.Join(" | ", group.Products.Select(p => p.ProductName).Take(3));
                group.AllStores = string.Join(", ", group.Products.Select(p => p.Store).Distinct());

                groups.Add(group);
            }
        }

        return groups
            .OrderByDescending(g => g.SiteCount)
            .ThenByDescending(g => g.PriceDifference ?? 0)
            .ThenBy(g => g.MinPrice ?? 999999)
            .ToList();
    }
}

public class ProductFeatures
{
    public string OriginalName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Capacity { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Color { get; set; } = "";
    public List<string> Properties { get; set; } = new();
}

public class SmartProductGroup
{
    public string Category { get; set; } = "";
    public string Capacity { get; set; } = "";
    public string KeyFeatures { get; set; } = "";
    public int ProductCount { get; set; }
    public int SiteCount { get; set; }
    public decimal? MinPrice { get; set; }
    public string MinPriceStore { get; set; } = "";
    public string MinPriceUrl { get; set; } = "";
    public decimal? MaxPrice { get; set; }
    public string MaxPriceStore { get; set; } = "";
    public decimal? PriceDifference { get; set; }
    public decimal? AvgPrice { get; set; }
    public string AllProductNames { get; set; } = "";
    public string AllStores { get; set; } = "";
    public List<ResultRow> Products { get; set; } = new();
}

public class ResultRow
{
    public string Store { get; set; } = "";
    public string SeedUrl { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "";
    public bool RequiresQuote { get; set; }
    public bool HasKDV { get; set; }
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