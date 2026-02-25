using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping;

public static class ScraperHelpers
{
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
        return p.EndsWith(".pdf") || p.EndsWith(".zip") || p.EndsWith(".rar") ||
               p.EndsWith(".xlsx") || p.EndsWith(".xls") || p.EndsWith(".doc") || p.EndsWith(".docx");
    }

    public static bool IsNavigationLink(string url, string text)
    {
        var lowerUrl = url.ToLowerInvariant();
        var lowerText = text.ToLowerInvariant();

        var navKeywords = new[]
        {
            "hakkımızda","iletisim","blog","hesap","account","giris","login",
            "kayit","register","sepet","cart","favoriler","wishlist",
            "yardim","help","sss","faq","nasil","how","kargo","shipping",
            "iade","return","kullanim","terms","gizlilik","privacy",
            "cerez","cookie","kvkk","sozlesme","agreement","kampanya",
            "teklif iste","numune","tedarikci","kariyer","career",
            "kurumsal","hakkinda","banka","katalog","siparis-takip",
            "uye-ol","giris-yap","hesabim","favorilerim"
        };

        if (navKeywords.Any(k => lowerUrl.Contains(k) || lowerText.Contains(k)))
            if (!Regex.IsMatch(text, @"\d{4,}") && text.Length < 60)
                return true;

        return false;
    }

    public static bool IsCategoryLink(string url, string text)
    {
        var lowerUrl = url.ToLowerInvariant();
        var lowerText = text.ToLowerInvariant();

        var categoryKeywords = new[]
        {
            "promosyon","urun","kategori","tisort","kalem","ajanda",
            "termos","kupa","defter","canta","sapka","powerbank",
            "usb","mouse","kartvizit","takvim","ofis","kirtasiye"
        };

        bool hasKeyword = categoryKeywords.Any(k => lowerUrl.Contains(k) || lowerText.Contains(k));
        return hasKeyword && !IsNavigationLink(url, text) && text.Length <= 100;
    }

    public static (decimal? price, string currency, bool requiresQuote, bool hasKdv, bool isPriceValid)
        ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, "", false, false, false);

        text = text.Replace("?", "TL").Replace("₺", "TL");
        var lowerText = text.ToLowerInvariant();

        bool requiresQuote = new[]
            { "teklif","iletisim","fiyat icin","bilgi icin","siparis","arayin","sorun","sor","contact","fiyat alınız" }
            .Any(k => lowerText.Contains(k));
        bool hasKdv = lowerText.Contains("+kdv") || lowerText.Contains("+ kdv") || lowerText.Contains("kdv");

        var measurementUnits = new[] { " lt", " ml", " kg", " gr", " cm", " mm", " adet" };
        if (measurementUnits.Any(u => lowerText.Contains(u)) &&
            !lowerText.Contains("tl") && !lowerText.Contains("try"))
            return (null, "", requiresQuote, hasKdv, false);

        var patterns = new[]
        {
            @"(\d{1,3}(?:\.\d{3})*,\d{2})\s*(TL|TRY|USD|EUR)?",
            @"(\d{1,3}(?:,\d{3})*\.\d{2})\s*(TL|TRY|USD|EUR)?",
            @"(\d+,\d{2})\s*(TL|TRY|USD|EUR)?",
            @"(\d+\.\d{2})\s*(TL|TRY|USD|EUR)?",
            @"(\d+)\s*(TL|TRY|USD|EUR)"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
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

            bool isValid = val >= 1.0m && val <= 100000m;
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

        productName = Regex.Replace(productName, @"\d+\s*adet", "", RegexOptions.IgnoreCase).Trim();
        productName = Regex.Replace(productName, @"\d+[.,]\d+\s*(TL|TRY)", "", RegexOptions.IgnoreCase).Trim();
        productName = Regex.Replace(productName, @"\+?KDV", "", RegexOptions.IgnoreCase).Trim();
        productName = Regex.Replace(productName, @"\(\d+\)", "").Trim();
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
        var h1 = await page.QuerySelectorAsync("h1");
        if (h1 != null)
        {
            var t = (await h1.InnerTextAsync())?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t!;
        }
        return (await page.TitleAsync())?.Trim() ?? "";
    }

    public static async Task<string[]> ExtractSameHostLinksAsync(IPage page, Uri baseUri)
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
        catch { }
        return links.Distinct().Take(30).ToList();
    }

    // ── Pagination linkleri keşfi ─────────────────────────────────────────────
    /// <summary>
    /// Sayfadaki pagination/sayfalama linklerini bulur.
    /// Sonraki sayfa, sayfa numaraları, "Sonraki"/"Next" butonları gibi
    /// yaygın kalıpları tanır.
    /// </summary>
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
                    '.pagination a',
                    '.pager a',
                    '.page-link',
                    'ul.pagination li a',
                    '.paginator a',
                    'nav[aria-label*=""page""] a',
                    'nav[aria-label*=""sayfa""] a',
                    '.page-numbers a',
                    '.pages a',
                    '[class*=""pagination""] a',
                    '[class*=""paging""] a',
                    '[class*=""sayfa""] a',
                    '.pageNav a',
                    '.page_nav a',
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

                // 3) Metin bazlı: Sonraki, İleri, Next, », ›, >
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
        catch { }
        return links.Distinct().ToList();
    }
}