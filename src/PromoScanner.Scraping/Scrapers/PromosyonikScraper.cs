using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public sealed class PromosyonikScraper : ISiteScraper
{
    public string HostPattern => "promosyonik";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".product-card-inner", new() { Timeout = 5000 }); }
        catch (PlaywrightException) { /* Timeout beklenen durum */ }

        var jsListing = @"() => {
            const results = [];
            document.querySelectorAll('.product-card-inner').forEach(card => {
                const linkEl = card.querySelector(""a[href*='/urun/']"");
                if (!linkEl) return;
                const href = linkEl.href || '';
                if (!href) return;

                // İsim: title attr → innerText → href slug
                let name = (linkEl.getAttribute('title') || '').trim();
                if (!name) name = (linkEl.innerText || '').replace(/\s+/g, ' ').trim();
                if (!name) {
                    const seg = href.split('/').pop() || '';
                    const parts = seg.split('-');
                    const nameParts = [];
                    for (const p of parts) {
                        if (/^[A-Z0-9]{2,}$/.test(p)) break;
                        nameParts.push(p);
                    }
                    if (nameParts.length === 0) {
                        for (let i = 0; i < Math.max(1, parts.length - 2); i++) nameParts.push(parts[i]);
                    }
                    name = nameParts.join(' ');
                    if (name.length > 0) name = name.charAt(0).toUpperCase() + name.slice(1);
                }
                if (!name || name.length < 3) return;

                // Fiyat
                const priceEl = card.querySelector('.product-card-price');
                let priceText = priceEl ? (priceEl.innerText || '').trim() : '';
                priceText = priceText.replace(/\?/g, 'TL');

                // Min sipariş
                let minQty = '';
                const cardText = card.innerText || '';
                const mq = cardText.match(/min\.?\s*sipari[sz]\s*(?:adedi)?[:\s]+(\d+)/i)
                        || cardText.match(/minimum[:\s]+(\d+)\s*adet/i)
                        || cardText.match(/en\s+az\s+(\d+)\s+adet/i)
                        || cardText.match(/(\d+)\s*adet\s*(?:ve\s+)?[uü]zeri/i);
                if (mq) minQty = mq[1];

                results.push([href, name.substring(0, 150), priceText, minQty]);
            });
            return results.slice(0, 300);
        }";

        var items = await page.EvaluateAsync<string[][]>(jsListing);

        foreach (var item in items ?? Array.Empty<string[]>())
        {
            if (item.Length < 2) continue;
            var href = item[0];
            var name = item[1];
            var priceText = item.Length > 2 ? item[2] : "";
            var minQtyStr = item.Length > 3 ? item[3] : "";

            if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;
            if (ScraperHelpers.IsNavigationLink(href, name)) continue;

            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);

            int.TryParse(minQtyStr, out var minQty);
            if (minQty < 1) minQty = 1;

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(ScraperHelpers.CreateRow(store, seedUrl, abs, category, name, p, ccy, quote, kdv, minQty));
        }
        return rows;
    }
}