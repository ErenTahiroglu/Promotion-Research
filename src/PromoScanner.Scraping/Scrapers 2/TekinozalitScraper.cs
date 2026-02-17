using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class TekinozalitScraper : ISiteScraper
{
    public string HostPattern => "tekinozalit";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".relative.flex.flex-col", new() { Timeout = 5000 }); } catch { }

        foreach (var card in await page.QuerySelectorAllAsync(".relative.flex.flex-col"))
        {
            var linkEl = await card.QuerySelectorAsync("a[href*='/']");
            var nameEl = await card.QuerySelectorAsync(".text-brand-pink-02, .line-clamp-1, span");
            if (linkEl == null || nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await linkEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await card.QuerySelectorAsync("span.font-medium, .price");
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