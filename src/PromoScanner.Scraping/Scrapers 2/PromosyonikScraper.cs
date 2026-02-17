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
            var nameEl = await card.QuerySelectorAsync(".product-card-text a, a[title]");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(ScraperHelpers.CreateRow(store, seedUrl, abs, category, name, null, "TRY", true, false));
        }
        return rows;
    }
}