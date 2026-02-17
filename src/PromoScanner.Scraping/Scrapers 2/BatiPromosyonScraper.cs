using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class BatiPromosyonScraper : ISiteScraper
{
    public string HostPattern => "batipromosyon";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".product-card", new() { Timeout = 5000 }); } catch { }

        foreach (var card in await page.QuerySelectorAllAsync(".product-card"))
        {
            var nameEl = await card.QuerySelectorAsync("a[href*='/urun']");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await card.QuerySelectorAsync(".product-card__price--new, .product-card__price");
            var priceText = priceEl != null ? (await priceEl.InnerTextAsync())?.Trim() : "";
            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText ?? "");
            if (!valid && p.HasValue) continue;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(ScraperHelpers.CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv));
            }
        }
        return rows;
    }
}