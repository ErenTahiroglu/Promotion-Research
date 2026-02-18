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
            "hakkımızda","iletişim","blog","hesap","account","giriş","login",
            "kayıt","register","sepet","cart","favoriler","wishlist",
            "yardım","help","sss","faq","nasıl","how","kargo","shipping",
            "iade","return","kullanım","terms","gizlilik","privacy",
            "çerez","cookie","kvkk","sözleşme","agreement","kampanya",
            "teklif iste","numune","tedarikçi","kariyer","career",
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

        var lowerText = text.ToLowerInvariant();
        bool requiresQuote = new[]
            { "teklif","iletişim","fiyat için","bilgi için","sipariş","arayın","sorun","sor","contact","fiyat alınız" }
            .Any(k => lowerText.Contains(k));
        bool hasKdv = lowerText.Contains("+kdv") || lowerText.Contains("+ kdv") || lowerText.Contains("kdv");

        var measurementUnits = new[] { " lt", " ml", " kg", " gr", " cm", " mm", " adet" };
        if (measurementUnits.Any(u => lowerText.Contains(u)) &&
            !lowerText.Contains("tl") && !lowerText.Contains("₺") && !lowerText.Contains("try"))
            return (null, "", requiresQuote, hasKdv, false);

        // ₺ encoding bozukluğu düzelt
        text = text.Replace("?", "₺");

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
            if (cur is "TL" or "₺") cur = "TRY";
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
        productName = Regex.Replace(productName, @"\d+[.,]\d+\s*(TL|₺|TRY)", "", RegexOptions.IgnoreCase).Trim();
        productName = Regex.Replace(productName, @"\+?KDV", "", RegexOptions.IgnoreCase).Trim();
        productName = Regex.Replace(productName, @"\(\d+\)", "").Trim();
        return productName;
    }

    public static ResultRow CreateRow(string store, string seedUrl, string url, string category,
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

            // Navigasyon sayfalarını atla
            if (IsNavigationLink(nu, "")) continue;

            // Sayfalama parametrelerini atla (?i=, ?page=, ?sort= vs.)
            if (u.Query.Length > 0 &&
                (u.Query.Contains("i=") || u.Query.Contains("page=") ||
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
}