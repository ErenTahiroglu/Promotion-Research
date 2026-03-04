using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public sealed class BidoluBaskiScraper : ISiteScraper
{
    public string HostPattern => "bidolubaski";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".flex.flex-col, .swiper-slide", new() { Timeout = 5000 }); }
        catch (PlaywrightException) { /* Timeout beklenen durum */ }

        var jsListing = $$"""
            () => {
                {{ScraperHelpers.MinOrderJsSnippet}}
                const results = [];
                document.querySelectorAll('.flex.flex-col, .swiper-slide').forEach(card => {
                    const nameEl = card.querySelector("a[href*='/']");
                    if (!nameEl) return;
                    const name = (nameEl.innerText || '').replace(/\s+/g, ' ').trim();
                    const href = nameEl.href || '';
                    if (!href || !name || name.length < 5) return;
                    const lowerHref = href.toLowerCase();
                    const lowerName = name.toLowerCase();
                    const navKw = ['hakkimizda','iletisim','blog','hesap','giris','sepet','yardim','sss','kargo'];
                    if (navKw.some(k => lowerHref.includes(k) || lowerName.includes(k))) return;
                    const priceEl = card.querySelector('span.font-semibold, .text-lg');
                    const priceText = priceEl ? (priceEl.innerText || '').trim() : '';
                    const minQty = extractMinOrder(card.innerText || '');
                    results.push([href, name.substring(0, 150), priceText, minQty]);
                });
                return results.slice(0, 300);
            }
            """;

        var items = await page.EvaluateAsync<string[][]>(jsListing);

        foreach (var item in items ?? [])
        {
            if (item.Length < 2) continue;
            var href = item[0];
            var name = item[1];
            var priceText = item.Length > 2 ? item[2] : "";
            var minQtyStr = item.Length > 3 ? item[3] : "";

            if (string.IsNullOrWhiteSpace(name) || name.Length < 5) continue;
            if (ScraperHelpers.IsNavigationLink(href, name)) continue;

            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
            if (!valid && p.HasValue) continue;

            int.TryParse(minQtyStr, out var minQty);
            if (minQty < 1) minQty = 1;

            if (p.HasValue || quote)
            {
                var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
                rows.Add(ScraperHelpers.CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv, minQty));
            }
        }
        return rows;
    }
}