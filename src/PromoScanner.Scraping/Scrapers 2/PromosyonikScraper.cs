using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class PromosyonikScraper : ISiteScraper
{
    public string HostPattern => "promosyonik";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".product-card-inner", new() { Timeout = 5000 }); } catch { }

        foreach (var card in await page.QuerySelectorAllAsync(".product-card-inner"))
        {
            // Link elementi
            var linkEl = await card.QuerySelectorAsync("a[href*='/urun/']");
            if (linkEl == null) continue;

            var href = await linkEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href)) continue;

            // Isim: once title attribute, sonra inner text, sonra href'den parse et
            var name = (await linkEl.GetAttributeAsync("title"))?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                name = (await linkEl.InnerTextAsync())?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                // href'den son segment: /urun/koseli-kursun-kalem-klm-t-3582
                var seg = href.Split('/').LastOrDefault() ?? "";
                // tire ile ayrilmis slug'i okunabilir hale getir, son kod parcasini at
                var parts = seg.Split('-');
                // sondaki kod parcasini at (KLM-T-3582 gibi)
                var nameParts = parts.TakeWhile(p => !System.Text.RegularExpressions.Regex.IsMatch(p, @"^[A-Z0-9]{2,}$")).ToList();
                if (nameParts.Count == 0) nameParts = parts.Take(Math.Max(1, parts.Length - 2)).ToList();
                name = string.Join(" ", nameParts);
                // Her kelimenin ilk harfini buyut
                name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
            }

            if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;

            // Fiyat
            var priceEl = await card.QuerySelectorAsync(".product-card-price");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() ?? "" : "";
            priceText = priceText.Replace("?", "TL");

            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(ScraperHelpers.CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
        }
        return rows;
    }
}