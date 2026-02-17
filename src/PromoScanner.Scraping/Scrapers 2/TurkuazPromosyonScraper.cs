using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class TurkuazPromosyonScraper : ISiteScraper
{
    public string HostPattern => "turkuazpromosyon";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".left, .product-block", new() { Timeout = 5000 }); } catch { }

        foreach (var item in await page.QuerySelectorAllAsync(".left, .product-block"))
        {
            var nameEl = await item.QuerySelectorAsync("h3.name a, .name a");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await item.QuerySelectorAsync(".price-gruop, .price .special-price");
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