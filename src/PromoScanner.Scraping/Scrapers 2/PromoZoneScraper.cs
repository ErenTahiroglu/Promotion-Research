using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class PromoZoneScraper : ISiteScraper
{
    public string HostPattern => "promozone";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync("div[class*='product-item'], .swiper-slide", new() { Timeout = 5000 }); } catch { }

        foreach (var item in await page.QuerySelectorAllAsync("div[class*='product-item'], .swiper-slide"))
        {
            var nameEl = await item.QuerySelectorAsync("a[href*='/urun/']");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name) || name.Length < 5) continue;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(ScraperHelpers.CreateRow(store, seedUrl, abs, category, name, null, "TRY", true, false));
        }
        return rows;
    }
}