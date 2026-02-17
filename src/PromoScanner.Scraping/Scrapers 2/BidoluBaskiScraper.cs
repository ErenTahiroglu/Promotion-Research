using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class BidoluBaskiScraper : ISiteScraper
{
    public string HostPattern => "bidolubaski";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".flex.flex-col, .swiper-slide", new() { Timeout = 5000 }); } catch { }

        foreach (var card in await page.QuerySelectorAllAsync(".flex.flex-col, .swiper-slide"))
        {
            var nameEl = await card.QuerySelectorAsync("a[href*='/']");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;
            if (ScraperHelpers.IsNavigationLink(href, name) || name.Length < 5) continue;

            var priceEl = await card.QuerySelectorAsync("span.font-semibold, .text-lg");
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