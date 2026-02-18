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

        var bodyText = await page.InnerTextAsync("body");
        if (bodyText.Contains("bulunmamaktadir") || bodyText.Contains("bulunamad"))
            return rows;

        try { await page.WaitForSelectorAsync("a[href*='/urun/']", new() { Timeout = 6000 }); } catch { }

        var items = await page.EvaluateAsync<string[][]>(@"() => {
            const seen = new Set();
            const results = [];
            document.querySelectorAll('a[href*=""/urun/""]').forEach(a => {
                const href = a.href || '';
                if (!href.includes('/urun/')) return;
                if (seen.has(href)) return;
                seen.add(href);
                const card = a.closest('li, article') || a.parentElement;
                let name = a.getAttribute('title') || '';
                if (!name) {
                    const img = card ? card.querySelector('img') : null;
                    name = img ? (img.getAttribute('alt') || '') : '';
                }
                if (!name) name = (a.innerText || '').replace(/\s+/g,' ').trim();
                if (!name && card) {
                    const textEl = card.querySelector('span, p, h2, h3, h4');
                    name = textEl ? (textEl.innerText || '').replace(/\s+/g,' ').trim() : '';
                }
                let price = '';
                if (card) {
                    const priceEl = card.querySelector('[class*=""price""], [class*=""fiyat""], [class*=""semibold""], [class*=""medium""]');
                    price = priceEl ? (priceEl.innerText || '').trim() : '';
                }
                if (name.length > 2) results.push([href, name.slice(0, 150), price]);
            });
            return results.slice(0, 150);
        }");

        foreach (var item in items ?? Array.Empty<string[]>())
        {
            if (item.Length < 2) continue;
            var href = item[0];
            var name = item[1];
            var priceText = item.Length > 2 ? item[2] : "";

            if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;
            if (ScraperHelpers.IsNavigationLink(href, name)) continue;

            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
            if (!valid && p.HasValue) continue;

            rows.Add(ScraperHelpers.CreateRow(store, seedUrl, href, category, name, p, ccy, quote, kdv));
        }
        return rows;
    }
}