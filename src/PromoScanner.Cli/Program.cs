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
                    if (!visited.Contains(cl)) q.Enqueue((seed, cl));
            }
            else
            {
                LogFile($"[SKIP] {url} - ne urun ne kategori");
            }
        }

        var links = await ScraperHelpers.ExtractSameHostLinksAsync(page, u);
        foreach (var link in links)
            if (!visited.Contains(link)) q.Enqueue((seed, link));
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

// ── Dosya çıktıları ────────────────────────────────────────────────────────────
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

List<SmartProductGroup> smartGroups = new();

if (pricedProducts.Count > 0)
{
    LogFile("Akilli karsilastirma yapiliyor...");
    smartGroups = SmartProductMatcher.GroupSimilarProducts(pricedProducts);
    WriteCsv(Path.Combine(outDir, "smart_comparison.csv"), smartGroups);
    LogFile($"Karsilastirma: {smartGroups.Count} grup, {smartGroups.Count(g => g.SiteCount >= 2)} tanesi 2+ sitede");

    var bestDeals = smartGroups
        .Where(g => g.SiteCount >= 2)
        .OrderByDescending(g => g.PriceDifference ?? 0)
        .Take(50)
        .ToList();

    if (bestDeals.Count > 0)
        WriteCsv(Path.Combine(outDir, "best_deals.csv"), bestDeals);
}

// ── Excel Raporu ──────────────────────────────────────────────────────────────
LogFile("Excel raporu yaziliyor...");
var excelPath = Path.Combine(outDir, "PromoScanner_Rapor.xlsx");
WriteExcelReport(excelPath, validProducts, pricedProducts, quoteProducts, smartGroups);
LogFile($"Excel: {excelPath}");

// ── Özet ──────────────────────────────────────────────────────────────────────
LogFile("===== OZET =====");
LogFile($"Taranan sayfa : {visited.Count}");
LogFile($"Gecerli urun  : {validProducts.Count}");
LogFile($"Fiyatli urun  : {pricedProducts.Count}");
LogFile($"Teklif gereken: {quoteProducts.Count}");
LogFile($"Basarisiz     : {failed.Count}");
LogFile($"Cikti klasoru : {outDir}");

// ─────────────────────────────────────────────────────────────────────────────
// Excel raporu oluşturan metot
// ─────────────────────────────────────────────────────────────────────────────
static void WriteExcelReport(
    string filePath,
    List<ResultRow> validProducts,
    List<ResultRow> pricedProducts,
    List<ResultRow> quoteProducts,
    List<SmartProductGroup> smartGroups)
{
    using var wb = new XLWorkbook();

    // Renk paleti
    var headerBg = XLColor.FromHtml("#1F4E79");   // koyu mavi
    var altRowBg = XLColor.FromHtml("#EBF3FB");   // açık mavi
    var dealBg = XLColor.FromHtml("#E2EFDA");   // açık yeşil (fırsatlar)
    var warnBg = XLColor.FromHtml("#FFF2CC");   // sarı (yüksek fark)
    var quoteBg = XLColor.FromHtml("#FCE4D6");   // açık turuncu

    // ── Sayfa 1: Özet Dashboard ───────────────────────────────────────────────
    {
        var ws = wb.Worksheets.Add("Ozet");
        ws.SheetView.FreezeRows(1);

        // Başlık bloğu
        ws.Cell("B2").Value = "PromoScanner — Tarama Özeti";
        ws.Cell("B2").Style.Font.FontSize = 20;
        ws.Cell("B2").Style.Font.Bold = true;
        ws.Cell("B2").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        ws.Range("B2:G2").Merge();

        ws.Cell("B3").Value = $"Oluşturulma: {DateTimeOffset.Now:dd.MM.yyyy HH:mm}";
        ws.Cell("B3").Style.Font.Italic = true;
        ws.Cell("B3").Style.Font.FontColor = XLColor.Gray;

        // İstatistik kartları
        var stats = new (string label, int value, string color)[]
        {
            ("Geçerli Ürün",      validProducts.Count,  "#2E75B6"),
            ("Fiyatlı Ürün",      pricedProducts.Count, "#70AD47"),
            ("Teklif Gereken",    quoteProducts.Count,  "#ED7D31"),
            ("Karşılaştırma Grubu", smartGroups.Count,  "#7030A0"),
            ("2+ Sitede Eşleşme",
                smartGroups.Count(g => g.SiteCount >= 2), "#C00000"),
        };

        int col = 2;
        ws.Cell(5, col - 1).Value = "Metrik";
        ws.Cell(5, col).Value = "Değer";
        ws.Row(5).Style.Font.Bold = true;

        for (int i = 0; i < stats.Length; i++)
        {
            var (label, value, color) = stats[i];
            ws.Cell(6 + i, col - 1).Value = label;
            ws.Cell(6 + i, col).Value = value;
            ws.Cell(6 + i, col).Style.Font.Bold = true;
            ws.Cell(6 + i, col).Style.Font.FontColor = XLColor.FromHtml(color);
        }

        // Site bazlı ürün sayısı
        ws.Cell(5, 5).Value = "Site";
        ws.Cell(5, 6).Value = "Ürün Sayısı";
        ws.Cell(5, 7).Value = "Fiyatlı";
        ws.Row(5).Style.Font.Bold = true;

        var siteStats = validProducts
            .GroupBy(p => p.Store)
            .Select(g => (
                Site: g.Key,
                Total: g.Count(),
                Priced: g.Count(r => r.Price.HasValue && r.Price > 0)))
            .OrderByDescending(x => x.Total)
            .ToList();

        for (int i = 0; i < siteStats.Count; i++)
        {
            ws.Cell(6 + i, 5).Value = siteStats[i].Site;
            ws.Cell(6 + i, 6).Value = siteStats[i].Total;
            ws.Cell(6 + i, 7).Value = siteStats[i].Priced;
        }

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 3; // sol boşluk
    }

    // ── Sayfa 2: Tüm Ürünler ─────────────────────────────────────────────────
    {
        var ws = wb.Worksheets.Add("Tum Urunler");
        ws.SheetView.FreezeRows(1);

        var headers = new[] { "Mağaza", "Kategori", "Ürün Adı", "Fiyat", "Para Birimi",
                               "KDV Dahil mi", "Min. Sipariş", "Teklif mi", "URL", "Zaman" };
        SetHeaders(ws, 1, headers, headerBg);

        for (int i = 0; i < validProducts.Count; i++)
        {
            var r = validProducts[i];
            int row = i + 2;

            ws.Cell(row, 1).Value = r.Store;
            ws.Cell(row, 2).Value = r.Category;
            ws.Cell(row, 3).Value = r.ProductName;
            ws.Cell(row, 4).Value = r.Price.HasValue ? (double)r.Price.Value : 0;
            ws.Cell(row, 5).Value = r.Currency;
            ws.Cell(row, 6).Value = r.HasKDV ? "Evet" : "Hayır";
            ws.Cell(row, 7).Value = r.MinOrderQty;
            ws.Cell(row, 8).Value = r.RequiresQuote ? "Evet" : "Hayır";
            ws.Cell(row, 9).Value = r.Url;
            ws.Cell(row, 10).Value = r.Timestamp.ToString("dd.MM.yyyy HH:mm");

            // Fiyat formatı
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";

            // URL hyperlink
            if (Uri.TryCreate(r.Url, UriKind.Absolute, out var uri))
                ws.Cell(row, 9).SetHyperlink(new XLHyperlink(uri.ToString()));

            // Satır rengi
            if (r.RequiresQuote)
                SetRowColor(ws, row, headers.Length, quoteBg);
            else if (i % 2 == 1)
                SetRowColor(ws, row, headers.Length, altRowBg);
        }

        ApplyTableStyle(ws, 1, validProducts.Count + 1, headers.Length);
        ws.Columns().AdjustToContents();
        ws.Column(9).Width = 50; // URL
    }

    // ── Sayfa 3: Fiyat Karşılaştırması ───────────────────────────────────────
    {
        var ws = wb.Worksheets.Add("Karsilastirma");
        ws.SheetView.FreezeRows(1);

        var headers = new[] { "Kategori", "Kapasite", "Özellikler", "Grup Sayısı",
                               "Site Sayısı", "En Düşük Fiyat", "En Düşük Site",
                               "En Yüksek Fiyat", "Fiyat Farkı", "Ort. Fiyat",
                               "Min. Sipariş", "Ürün Adları", "Siteler" };
        SetHeaders(ws, 1, headers, headerBg);

        for (int i = 0; i < smartGroups.Count; i++)
        {
            var g = smartGroups[i];
            int row = i + 2;

            ws.Cell(row, 1).Value = g.Category;
            ws.Cell(row, 2).Value = g.Capacity;
            ws.Cell(row, 3).Value = g.KeyFeatures;
            ws.Cell(row, 4).Value = g.ProductCount;
            ws.Cell(row, 5).Value = g.SiteCount;
            ws.Cell(row, 6).Value = g.MinPrice.HasValue ? (double)g.MinPrice.Value : 0;
            ws.Cell(row, 7).Value = g.MinPriceStore;
            ws.Cell(row, 8).Value = g.MaxPrice.HasValue ? (double)g.MaxPrice.Value : 0;
            ws.Cell(row, 9).Value = g.PriceDifference.HasValue ? (double)g.PriceDifference.Value : 0;
            ws.Cell(row, 10).Value = g.AvgPrice.HasValue ? (double)g.AvgPrice.Value : 0;
            ws.Cell(row, 11).Value = g.MinOrderQty;
            ws.Cell(row, 12).Value = g.AllProductNames;
            ws.Cell(row, 13).Value = g.AllStores;

            // Fiyat formatları
            foreach (int c in new[] { 6, 8, 9, 10 })
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";

            // Renk: 2+ sitede eşleşme → yeşil; yüksek fark → sarı
            if (g.SiteCount >= 2 && g.PriceDifference > 50)
                SetRowColor(ws, row, headers.Length, warnBg);
            else if (g.SiteCount >= 2)
                SetRowColor(ws, row, headers.Length, dealBg);
            else if (i % 2 == 1)
                SetRowColor(ws, row, headers.Length, altRowBg);
        }

        ApplyTableStyle(ws, 1, smartGroups.Count + 1, headers.Length);
        ws.Columns().AdjustToContents();
        ws.Column(12).Width = 60;
        ws.Column(13).Width = 40;
    }

    // ── Sayfa 4: En İyi Fırsatlar ─────────────────────────────────────────────
    {
        var bestDeals = smartGroups
            .Where(g => g.SiteCount >= 2 && g.PriceDifference > 0)
            .OrderByDescending(g => g.PriceDifference)
            .Take(50)
            .ToList();

        var ws = wb.Worksheets.Add("En Iyi Firsatlar");
        ws.SheetView.FreezeRows(1);

        var headers = new[] { "Sıra", "Kategori", "En Düşük Fiyat", "Mağaza",
                               "En Yüksek Fiyat", "Fiyat Farkı", "Fark %",
                               "Site Sayısı", "Ürün Adı", "URL" };
        SetHeaders(ws, 1, headers, XLColor.FromHtml("#C00000")); // kırmızı başlık

        for (int i = 0; i < bestDeals.Count; i++)
        {
            var g = bestDeals[i];
            int row = i + 2;

            double farkYuzde = (g.MaxPrice.HasValue && g.MaxPrice > 0 && g.PriceDifference.HasValue)
                ? Math.Round((double)(g.PriceDifference.Value / g.MaxPrice.Value) * 100, 1)
                : 0;

            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = g.Category;
            ws.Cell(row, 3).Value = g.MinPrice.HasValue ? (double)g.MinPrice.Value : 0;
            ws.Cell(row, 4).Value = g.MinPriceStore;
            ws.Cell(row, 5).Value = g.MaxPrice.HasValue ? (double)g.MaxPrice.Value : 0;
            ws.Cell(row, 6).Value = g.PriceDifference.HasValue ? (double)g.PriceDifference.Value : 0;
            ws.Cell(row, 7).Value = farkYuzde;
            ws.Cell(row, 8).Value = g.SiteCount;
            ws.Cell(row, 9).Value = g.AllProductNames;
            ws.Cell(row, 10).Value = g.MinPriceUrl;

            foreach (int c in new[] { 3, 5, 6 })
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 7).Style.NumberFormat.Format = "0.0\"%\"";

            if (Uri.TryCreate(g.MinPriceUrl, UriKind.Absolute, out var uri))
                ws.Cell(row, 10).SetHyperlink(new XLHyperlink(uri.ToString()));

            // İlk 10 = en iyi fırsatlar, öne çıkar
            if (i < 10)
                SetRowColor(ws, row, headers.Length, dealBg);
            else if (i % 2 == 1)
                SetRowColor(ws, row, headers.Length, altRowBg);
        }

        ApplyTableStyle(ws, 1, bestDeals.Count + 1, headers.Length);
        ws.Columns().AdjustToContents();
        ws.Column(9).Width = 60;
        ws.Column(10).Width = 50;
    }

    // ── Sayfa 5: Teklif Gereken Ürünler ──────────────────────────────────────
    if (quoteProducts.Count > 0)
    {
        var ws = wb.Worksheets.Add("Teklif Gereken");
        ws.SheetView.FreezeRows(1);

        var headers = new[] { "Mağaza", "Kategori", "Ürün Adı", "Min. Sipariş", "URL", "Zaman" };
        SetHeaders(ws, 1, headers, XLColor.FromHtml("#ED7D31")); // turuncu başlık

        for (int i = 0; i < quoteProducts.Count; i++)
        {
            var r = quoteProducts[i];
            int row = i + 2;

            ws.Cell(row, 1).Value = r.Store;
            ws.Cell(row, 2).Value = r.Category;
            ws.Cell(row, 3).Value = r.ProductName;
            ws.Cell(row, 4).Value = r.MinOrderQty;
            ws.Cell(row, 5).Value = r.Url;
            ws.Cell(row, 6).Value = r.Timestamp.ToString("dd.MM.yyyy HH:mm");

            if (Uri.TryCreate(r.Url, UriKind.Absolute, out var uri))
                ws.Cell(row, 5).SetHyperlink(new XLHyperlink(uri.ToString()));

            if (i % 2 == 1) SetRowColor(ws, row, headers.Length, quoteBg);
        }

        ApplyTableStyle(ws, 1, quoteProducts.Count + 1, headers.Length);
        ws.Columns().AdjustToContents();
        ws.Column(5).Width = 50;
    }

    wb.SaveAs(filePath);
}

// ── Yardımcı metotlar ─────────────────────────────────────────────────────────
static void SetHeaders(IXLWorksheet ws, int row, string[] headers, XLColor bg)
{
    for (int c = 0; c < headers.Length; c++)
    {
        var cell = ws.Cell(row, c + 1);
        cell.Value = headers[c];
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Fill.BackgroundColor = bg;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }
}

static void SetRowColor(IXLWorksheet ws, int row, int colCount, XLColor color)
{
    ws.Range(row, 1, row, colCount).Style.Fill.BackgroundColor = color;
}

static void ApplyTableStyle(IXLWorksheet ws, int firstRow, int lastRow, int lastCol)
{
    if (lastRow <= firstRow) return;
    var range = ws.Range(firstRow, 1, lastRow, lastCol);
    range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    range.Style.Border.InsideBorder = XLBorderStyleValues.Hair;

    // Başlık satırı alt kenarlığı kalın
    ws.Range(firstRow, 1, firstRow, lastCol)
      .Style.Border.BottomBorder = XLBorderStyleValues.Medium;
}