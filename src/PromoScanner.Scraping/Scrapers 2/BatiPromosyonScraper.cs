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
            // İsim
            var nameEl = await card.QuerySelectorAsync(".product-card__name a, .product-card__name");
            if (nameEl == null) continue;
            var name = (await nameEl.InnerTextAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;

            // Link
            var linkEl = await card.QuerySelectorAsync("a[href]");
            var href = linkEl != null ? await linkEl.GetAttributeAsync("href") : null;
            if (string.IsNullOrWhiteSpace(href)) continue;

            // Fiyat
            var priceEl = await card.QuerySelectorAsync(".product-card__price--new, .product-card__prices");
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