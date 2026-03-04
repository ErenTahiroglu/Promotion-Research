using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public sealed class IlpenScraper : ISiteScraper
{
    public string HostPattern => "ilpen";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        // İlpen sayfaları JS ile render ediyor — biraz bekle
        await page.WaitForTimeoutAsync(1500);

        try
        {
            await page.WaitForSelectorAsync(
                ".laberProductGrid .item, .products-grid .item, .product-list .item",
                new() { Timeout = 7000 });
        }
        catch (PlaywrightException) { }

        var cardSelectors = new[]
        {
            ".laberProductGrid .item",
            ".products-grid .item",
            ".category-products .item",
            ".product-list li",
        };

        List<IElementHandle> cards = new();
        foreach (var sel in cardSelectors)
        {
            var found = await page.QuerySelectorAllAsync(sel);
            if (found.Count > 0) { cards = found.ToList(); break; }
        }

        if (cards.Count == 0)
            return await FallbackJsExtractAsync(page, pageUri, store, seedUrl, category);

        foreach (var item in cards)
        {
            var nameEl = await item.QuerySelectorAsync(
                "h2.productName a, .product-name a, h2 a, h3 a, .name a, a.product-item-link");

            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            // Fiyat
            string priceText = "";
            var priceSelectors = new[]
            {
                ".special-price .price",
                ".regular-price .price",
                ".price-box .price",
                ".product-price",
                "[class*='price'] .price",
                "[class*='fiyat']",
                ".price",
            };

            foreach (var pSel in priceSelectors)
            {
                var priceEl = await item.QuerySelectorAsync(pSel);
                if (priceEl == null) continue;
                var txt = (await priceEl.InnerTextAsync())?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(txt) && (txt.Contains("TL") || txt.Contains("₺") || System.Text.RegularExpressions.Regex.IsMatch(txt, @"\d")))
                {
                    priceText = txt;
                    break;
                }
            }

            // Min sipariş — kart içinde ara
            int minQty = 1;
            try
            {
                var cardText = (await item.InnerTextAsync())?.Trim() ?? "";
                var mqMatch = System.Text.RegularExpressions.Regex.Match(cardText,
                    @"min\.?\s*sipari[sz]\s*(?:adedi)?[:\s]+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!mqMatch.Success)
                    mqMatch = System.Text.RegularExpressions.Regex.Match(cardText,
                        @"minimum[:\s]+(\d+)\s*adet", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!mqMatch.Success)
                    mqMatch = System.Text.RegularExpressions.Regex.Match(cardText,
                        @"en\s+az\s+(\d+)\s+adet", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!mqMatch.Success)
                    mqMatch = System.Text.RegularExpressions.Regex.Match(cardText,
                        @"(\d+)\s*adet\s*(?:ve\s+)?[uü]zeri", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mqMatch.Success && int.TryParse(mqMatch.Groups[1].Value, out var mq) && mq > 0)
                    minQty = mq;
            }
            catch (PlaywrightException) { }

            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
            bool requiresQuote = quote || (!p.HasValue && string.IsNullOrWhiteSpace(priceText));

            var abs = Uri.TryCreate(pageUri, href, out var absUri) ? absUri!.ToString() : href!;
            rows.Add(ScraperHelpers.CreateRow(
                store, seedUrl, abs, category, name,
                valid ? p : null,
                string.IsNullOrEmpty(ccy) ? "TRY" : ccy,
                requiresQuote,
                kdv,
                minQty));
        }

        return rows;
    }

    // ── Fallback: JS ile tüm ürün linklerini topla ───────────────────────────
    private async Task<List<ResultRow>> FallbackJsExtractAsync(
        IPage page, Uri pageUri, string store, string seedUrl, string category)
    {
        var rows = new List<ResultRow>();

        var items = await page.EvaluateAsync<string[][]>(@"() => {
            const seen = new Set();
            const results = [];

            document.querySelectorAll('a[href]').forEach(a => {
                const href = a.href || '';
                if (!href.match(/\/(tr|en)\/\d+-/) && !href.includes('/product/')) return;
                if (seen.has(href)) return;
                seen.add(href);

                const card = a.closest('li, article, .item, [class*=""product""]') || a.parentElement;

                // İsim
                let name = a.getAttribute('title') || (a.innerText || '').replace(/\s+/g, ' ').trim();
                if (!name && card) {
                    const el = card.querySelector('h2, h3, h4, .name, [class*=""title""]');
                    name = el ? (el.innerText || '').replace(/\s+/g, ' ').trim() : '';
                }
                if (!name || name.length < 3) return;

                // Fiyat
                let price = '';
                if (card) {
                    const priceEl = card.querySelector('.special-price .price, .regular-price .price, .price-box .price, .price, [class*=""fiyat""]');
                    if (priceEl) price = (priceEl.innerText || '').trim();
                }

                // Min sipariş
                let minQty = '';
                if (card) {
                    const cardText = card.innerText || '';
                    const mq = cardText.match(/min\.?\s*sipari[sz]\s*(?:adedi)?[:\s]+(\d+)/i)
                            || cardText.match(/minimum[:\s]+(\d+)\s*adet/i)
                            || cardText.match(/en\s+az\s+(\d+)\s+adet/i)
                            || cardText.match(/(\d+)\s*adet\s*(?:ve\s+)?[uü]zeri/i);
                    if (mq) minQty = mq[1];
                }

                results.push([href, name.slice(0, 150), price, minQty]);
            });

            return results.slice(0, 200);
        }");

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
            bool requiresQuote = quote || (!p.HasValue && string.IsNullOrWhiteSpace(priceText));

            int.TryParse(minQtyStr, out var minQty);
            if (minQty < 1) minQty = 1;

            rows.Add(ScraperHelpers.CreateRow(
                store, seedUrl, href, category, name,
                valid ? p : null,
                string.IsNullOrEmpty(ccy) ? "TRY" : ccy,
                requiresQuote, kdv, minQty));
        }

        return rows;
    }
}