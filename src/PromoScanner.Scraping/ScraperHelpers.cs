using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping;

public static partial class ScraperHelpers
{
    // ── Derlenmiş regex kalıpları ─────────────────────────────────────────────
    [GeneratedRegex(@"(\d{1,3}(?:\.\d{3})*,\d{2})\s*(TL|TRY|USD|EUR)?", RegexOptions.IgnoreCase)]
    private static partial Regex PricePattern1();

    [GeneratedRegex(@"(\d{1,3}(?:,\d{3})*\.\d{2})\s*(TL|TRY|USD|EUR)?", RegexOptions.IgnoreCase)]
    private static partial Regex PricePattern2();

    [GeneratedRegex(@"(\d+,\d{2})\s*(TL|TRY|USD|EUR)?", RegexOptions.IgnoreCase)]
    private static partial Regex PricePattern3();

    [GeneratedRegex(@"(\d+\.\d{2})\s*(TL|TRY|USD|EUR)?", RegexOptions.IgnoreCase)]
    private static partial Regex PricePattern4();

    [GeneratedRegex(@"(\d+)\s*(TL|TRY|USD|EUR)", RegexOptions.IgnoreCase)]
    private static partial Regex PricePattern5();

    [GeneratedRegex(@"\d{4,}")]
    private static partial Regex FourDigitsPattern();

    [GeneratedRegex(@"\d+\s*adet", RegexOptions.IgnoreCase)]
    private static partial Regex AdetCleanPattern();

    [GeneratedRegex(@"\d+[.,]\d+\s*(TL|TRY)", RegexOptions.IgnoreCase)]
    private static partial Regex PriceCleanPattern();

    [GeneratedRegex(@"\+?KDV", RegexOptions.IgnoreCase)]
    private static partial Regex KdvCleanPattern();

    [GeneratedRegex(@"\(\d+\)")]
    private static partial Regex ParenDigitsPattern();

    // ── Static readonly arrays (tekrar tekrar oluşturulmasın) ─────────────────
    private static readonly Regex[] PricePatterns =
    [
        PricePattern1(), PricePattern2(), PricePattern3(), PricePattern4(), PricePattern5()
    ];

    private static readonly string[] QuoteKeywords =
    [
        "teklif", "iletisim", "fiyat icin", "bilgi icin", "siparis",
        "arayin", "sorun", "sor", "contact", "fiyat alınız"
    ];

    private static readonly string[] MeasurementUnits =
        [" lt", " ml", " kg", " gr", " cm", " mm", " adet"];

    private static readonly string[] NavKeywords =
    [
        "hakkımızda", "iletisim", "blog", "hesap", "account", "giris", "login",
        "kayit", "register", "sepet", "cart", "favoriler", "wishlist",
        "yardim", "help", "sss", "faq", "nasil", "how", "kargo", "shipping",
        "iade", "return", "kullanim", "terms", "gizlilik", "privacy",
        "cerez", "cookie", "kvkk", "sozlesme", "agreement", "kampanya",
        "teklif iste", "numune", "tedarikci", "kariyer", "career",
        "kurumsal", "hakkinda", "banka", "katalog", "siparis-takip",
        "uye-ol", "giris-yap", "hesabim", "favorilerim"
    ];

    private static readonly string[] CategoryKeywords =
    [
        "promosyon", "urun", "kategori", "tisort", "kalem", "ajanda",
        "termos", "kupa", "defter", "canta", "sapka", "powerbank",
        "usb", "mouse", "kartvizit", "takvim", "ofis", "kirtasiye"
    ];

    private static readonly string[] FileExtensions =
        [".pdf", ".zip", ".rar", ".xlsx", ".xls", ".doc", ".docx"];

    // ── Min sipariş JS snippet'i (scraperlar tarafından kullanılır) ────────────
    /// <summary>
    /// Scraperlar'daki JS kodunda kullanılabilecek ortak min sipariş regex kalıbı.
    /// </summary>
    public const string MinOrderJsSnippet = """
        function extractMinOrder(text) {
            var mq = text.match(/min\.?\s*sipari[sz]\s*(?:adedi)?[:\s]+(\d+)/i)
                  || text.match(/minimum[:\s]+(\d+)\s*adet/i)
                  || text.match(/en\s+az\s+(\d+)\s+adet/i)
                  || text.match(/(\d+)\s*adet\s*(?:ve\s+)?[uü]zeri/i);
            return mq ? mq[1] : '';
        }
        """;

    /// <summary>
    /// Detay sayfalarında ortak fiyat çekme JS kodu.
    /// Leaf elementleri tarayarak ₺/TL içeren fiyatları bulur.
    /// Çizili olanları liste fiyatı, diğerlerini net fiyat olarak ayırır.
    /// </summary>
    public const string DetailPriceJsSnippet = """
        function extractPrices() {
            var price = '';
            var listPrice = '';
            var allEls = document.querySelectorAll('*');
            for (var i = 0; i < allEls.length; i++) {
                var el = allEls[i];
                if (el.children.length > 0) continue;
                var txt = (el.innerText || '').trim();
                if (txt.length < 3 || txt.length > 30) continue;
                var hasCur = txt.indexOf('\u20ba') >= 0 || /\d+[.,]\d{2}\s*TL/i.test(txt);
                if (!hasCur) continue;
                var style = window.getComputedStyle(el).textDecoration || '';
                var tag = el.tagName.toLowerCase();
                var crossed = style.indexOf('line-through') >= 0 || tag === 'del' || tag === 's';
                if (crossed) { if (!listPrice) listPrice = txt; }
                else         { if (!price)     price     = txt; }
            }
            // Fallback: meta product:price
            if (!price) {
                var ogPrice = document.querySelector('meta[property="product:price:amount"]')
                           || document.querySelector('meta[property="og:price:amount"]');
                if (ogPrice) {
                    var pVal = ogPrice.getAttribute('content');
                    if (pVal && /^\d/.test(pVal)) price = pVal + ' TL';
                }
            }
            return { price: price, listPrice: listPrice };
        }
        """;

    /// <summary>
    /// Detay sayfalarında h1/meta/title'dan ürün adı çekme JS kodu.
    /// </summary>
    public const string DetailNameJsSnippet = """
        function extractProductName() {
            var name = '';
            var h1 = document.querySelector('h1');
            if (h1) {
                var t = (h1.innerText || '').replace(/\s+/g, ' ').trim();
                if (t.length > 2) name = t;
            }
            if (!name) {
                var og = document.querySelector('meta[property="og:title"]');
                if (og) name = (og.getAttribute('content') || '').trim();
            }
            if (!name) {
                var title = document.querySelector('title');
                if (title) name = (title.innerText || '').split('|')[0].split('-')[0].trim();
            }
            return name;
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════════

    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        url = url.Trim();
        var hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash];
        if (url.EndsWith("/") && url.Length > 8) url = url.TrimEnd('/');
        return url;
    }

    public static bool LooksLikeFileDownload(Uri u)
    {
        var p = u.AbsolutePath.ToLowerInvariant();
        return FileExtensions.Any(ext => p.EndsWith(ext, StringComparison.Ordinal));
    }

    public static bool IsNavigationLink(string url, string text)
    {
        var lowerUrl = url.ToLowerInvariant();
        var lowerText = text.ToLowerInvariant();

        if (NavKeywords.Any(k => lowerUrl.Contains(k) || lowerText.Contains(k)))
            if (!FourDigitsPattern().IsMatch(text) && text.Length < 60)
                return true;

        return false;
    }

    public static bool IsCategoryLink(string url, string text)
    {
        var lowerUrl = url.ToLowerInvariant();
        var lowerText = text.ToLowerInvariant();

        bool hasKeyword = CategoryKeywords.Any(k => lowerUrl.Contains(k) || lowerText.Contains(k));
        return hasKeyword && !IsNavigationLink(url, text) && text.Length <= 100;
    }

    public static (decimal? price, string currency, bool requiresQuote, bool hasKdv, bool isPriceValid)
        ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, "", false, false, false);

        text = text.Replace("?", "TL").Replace("₺", "TL");
        var lowerText = text.ToLowerInvariant();

        bool requiresQuote = QuoteKeywords.Any(k => lowerText.Contains(k));
        bool hasKdv = lowerText.Contains("+kdv") || lowerText.Contains("+ kdv") || lowerText.Contains("kdv");

        if (MeasurementUnits.Any(u => lowerText.Contains(u)) &&
            !lowerText.Contains("tl") && !lowerText.Contains("try"))
            return (null, "", requiresQuote, hasKdv, false);

        foreach (var pattern in PricePatterns)
        {
            var m = pattern.Match(text);
            if (!m.Success) continue;

            var numStr = m.Groups[1].Value;
            if (numStr.Contains('.') && numStr.Contains(','))
                numStr = numStr.LastIndexOf(',') > numStr.LastIndexOf('.')
                    ? numStr.Replace(".", "").Replace(',', '.')
                    : numStr.Replace(",", "");
            else if (numStr.Contains(','))
                numStr = numStr.Replace(',', '.');

            if (!decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) continue;

            var cur = (m.Groups[2].Value ?? "").ToUpperInvariant();
            if (cur == "TL") cur = "TRY";
            if (string.IsNullOrEmpty(cur)) cur = "TRY";

            bool isValid = val >= 0.10m && val <= 500_000m;
            return (val, cur, requiresQuote, hasKdv, isValid);
        }

        return (null, "", requiresQuote, hasKdv, false);
    }

    public static string CleanProductName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var productName = name.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .Select(l => l.Trim())
                              .FirstOrDefault() ?? "";

        productName = AdetCleanPattern().Replace(productName, "").Trim();
        productName = PriceCleanPattern().Replace(productName, "").Trim();
        productName = KdvCleanPattern().Replace(productName, "").Trim();
        productName = ParenDigitsPattern().Replace(productName, "").Trim();
        return productName;
    }

    public static ResultRow CreateRow(
        string store, string seedUrl, string url, string category,
        string productName, decimal? price, string currency,
        bool requiresQuote, bool hasKDV,
        int minOrderQty = 1, decimal? listPrice = null)
    {
        return new ResultRow
        {
            Store = store,
            SeedUrl = seedUrl,
            Url = url,
            Category = category,
            ProductName = CleanProductName(productName),
            Price = price,
            ListPrice = listPrice,
            Currency = currency,
            RequiresQuote = requiresQuote,
            HasKDV = hasKDV,
            MinOrderQty = minOrderQty,
            QuantityPriceListJson = "",
            Timestamp = DateTimeOffset.Now,
            Error = ""
        };
    }

    public static async Task<string> GetPageCategoryAsync(IPage page)
    {
        try
        {
            var h1 = await page.QuerySelectorAsync("h1");
            if (h1 != null)
            {
                var t = (await h1.InnerTextAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(t)) return t!;
            }
            return (await page.TitleAsync())?.Trim() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static async Task<string[]> ExtractSameHostLinksAsync(IPage page, Uri baseUri)
    {
        try
        {
            var hrefs = await page.EvaluateAsync<string[]>(
                @"() => Array.from(document.querySelectorAll('a[href]'))
                             .map(a => a.href).filter(Boolean)");

            var list = new List<string>();
            foreach (var h in hrefs)
            {
                if (!Uri.TryCreate(h, UriKind.Absolute, out var u)) continue;
                if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) continue;
                if (!string.Equals(u.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

                var nu = NormalizeUrl(u.ToString());
                if (IsNavigationLink(nu, "")) continue;
                if (u.Query.Length > 0 &&
                    (u.Query.Contains("i=") ||
                     u.Query.Contains("sort=") || u.Query.Contains("filter=")))
                    continue;

                list.Add(nu);
            }
            return list.Distinct().ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static async Task<List<string>> FindCategoryLinksAsync(IPage page, Uri baseUri)
    {
        var links = new List<string>();
        try
        {
            var allLinks = await page.QuerySelectorAllAsync("a[href]");
            foreach (var linkEl in allLinks.Take(200))
            {
                var href = await linkEl.GetAttributeAsync("href");
                var text = (await linkEl.InnerTextAsync())?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (!Uri.TryCreate(baseUri, href, out var absUri)) continue;
                if (!string.Equals(absUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsCategoryLink(absUri.ToString(), text))
                    links.Add(NormalizeUrl(absUri.ToString()));
            }
        }
        catch (PlaywrightException)
        {
            // Sayfa navigasyonu sırasında element kaybolabilir — beklenen durum
        }
        return links.Distinct().Take(30).ToList();
    }

    // ── Pagination linkleri keşfi ─────────────────────────────────────────────
    public static async Task<List<string>> FindPaginationLinksAsync(IPage page, Uri baseUri)
    {
        var links = new List<string>();
        try
        {
            var jsCode = @"() => {
                const results = new Set();
                const host = location.hostname;

                // 1) rel='next'
                document.querySelectorAll('a[rel=""next""]').forEach(a => {
                    if (a.href) results.add(a.href);
                });

                // 2) Yaygın pagination CSS selektörleri
                const selectors = [
                    '.pagination a', '.pager a', '.page-link',
                    'ul.pagination li a', '.paginator a',
                    'nav[aria-label*=""page""] a', 'nav[aria-label*=""sayfa""] a',
                    '.page-numbers a', '.pages a',
                    '[class*=""pagination""] a', '[class*=""paging""] a',
                    '[class*=""sayfa""] a', '.pageNav a', '.page_nav a',
                ];
                for (const sel of selectors) {
                    try {
                        document.querySelectorAll(sel).forEach(a => {
                            if (a.href) {
                                try { if (new URL(a.href).hostname === host) results.add(a.href); } catch{}
                            }
                        });
                    } catch{}
                }

                // 3) Metin bazlı: Sonraki, İleri, Next
                document.querySelectorAll('a[href]').forEach(a => {
                    const text = (a.innerText || '').trim();
                    if (/^(sonraki|ileri|next|»|›|>|>>|devam|daha fazla|more)$/i.test(text)) {
                        try { if (new URL(a.href).hostname === host) results.add(a.href); } catch{}
                    }
                });

                // 4) URL'de page/sayfa parametresi olan linkler
                document.querySelectorAll('a[href]').forEach(a => {
                    const href = a.href || '';
                    if (/[?&](page|sayfa|p|pg|pagenum)=\d+/i.test(href) ||
                        /\/(page|sayfa)\/\d+/i.test(href)) {
                        try { if (new URL(href).hostname === host) results.add(href); } catch{}
                    }
                });

                return [...results];
            }";

            var hrefs = await page.EvaluateAsync<string[]>(jsCode);
            foreach (var h in hrefs ?? Array.Empty<string>())
            {
                if (!Uri.TryCreate(h, UriKind.Absolute, out var u)) continue;
                if (!string.Equals(u.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
                var nu = NormalizeUrl(u.ToString());
                if (!string.IsNullOrWhiteSpace(nu))
                    links.Add(nu);
            }
        }
        catch (PlaywrightException)
        {
            // Sayfa navigasyonu sırasında sayfa kapanmış olabilir — beklenen durum
        }
        return links.Distinct().ToList();
    }
}