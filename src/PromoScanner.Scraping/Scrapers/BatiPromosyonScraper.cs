using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public sealed class BatiPromosyonScraper : ISiteScraper
{
    public string HostPattern => "batipromosyon";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        if (IsDetailPage(pageUri))
            return await ExtractDetailAsync(page, pageUri, seedUrl);

        return await ExtractListingAsync(page, pageUri, seedUrl);
    }

    private static bool IsDetailPage(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        // Batı detay URL'leri: /urunler/kategori/urun-adi-123 (sonunda sayı)
        return Regex.IsMatch(path, @"-\d{2,}$") || Regex.IsMatch(path, @"/\d{3,}[A-Z]{0,5}$");
    }

    // ── Listeleme sayfası ─────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractListingAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".product-card", new() { Timeout = 5000 }); } catch { }

        var jsListing = @"() => {
            const results = [];
            document.querySelectorAll('.product-card').forEach(card => {
                const nameEl = card.querySelector('.product-card__name a, .product-card__name');
                if (!nameEl) return;
                const name = (nameEl.innerText || '').replace(/\s+/g, ' ').trim();
                if (!name || name.length < 3) return;

                const linkEl = card.querySelector('a[href]');
                const href = linkEl ? linkEl.href : '';
                if (!href) return;

                // Fiyat
                const priceEl = card.querySelector('.product-card__price--new, .product-card__prices');
                const priceText = priceEl ? (priceEl.innerText || '').trim() : '';

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

    // ── Ürün detay sayfası ────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractDetailAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;

        await page.WaitForTimeoutAsync(1000);

        try
        {
            await page.WaitForFunctionAsync(
                @"() => {
                    var body = document.body.innerText || '';
                    return body.indexOf('₺') >= 0 || body.indexOf('TL') >= 0
                        || /\d+[.,]\d{2}/.test(body);
                }",
                null,
                new PageWaitForFunctionOptions { Timeout = 6000 });
        }
        catch (PlaywrightException) { }

        var jsDetail = @"() => {
            var name = '';
            var h1 = document.querySelector('h1');
            if (h1) {
                var t = (h1.innerText || '').replace(/\s+/g, ' ').trim();
                if (t.length > 2) name = t;
            }
            if (!name) {
                var og = document.querySelector('meta[property=""og:title""]');
                if (og) name = (og.getAttribute('content') || '').trim();
            }
            if (!name) {
                var title = document.querySelector('title');
                if (title) name = (title.innerText || '').split('|')[0].split('-')[0].trim();
            }
            if (!name || name.length < 3) return null;

            var price = '';
            var listPrice = '';
            var priceSelectors = [
                '.product-card__price--new', '.product-price',
                '.special-price', '.price-new', '.price-box .price',
                '[class*=""fiyat""]', '[class*=""price""]',
            ];
            for (var sel of priceSelectors) {
                var el = document.querySelector(sel);
                if (!el) continue;
                var txt = (el.innerText || '').trim();
                if (txt.length > 0 && (txt.indexOf('₺') >= 0 || txt.indexOf('TL') >= 0 || /\d+[.,]\d{2}/.test(txt))) {
                    price = txt;
                    break;
                }
            }

            if (!price) {
                var allEls = document.querySelectorAll('*');
                for (var i = 0; i < allEls.length; i++) {
                    var el = allEls[i];
                    if (el.children.length > 0) continue;
                    var txt = (el.innerText || '').trim();
                    if (txt.length < 3 || txt.length > 30) continue;
                    var hasCur = txt.indexOf('₺') >= 0 || /\d+[.,]\d{2}\s*TL/i.test(txt);
                    if (!hasCur) continue;
                    var style = window.getComputedStyle(el).textDecoration || '';
                    var tag = el.tagName.toLowerCase();
                    var crossed = style.indexOf('line-through') >= 0 || tag === 'del' || tag === 's';
                    if (crossed) { if (!listPrice) listPrice = txt; }
                    else         { if (!price)     price     = txt; }
                }
            }

            if (!price) {
                var ogPrice = document.querySelector('meta[property=""product:price:amount""]')
                           || document.querySelector('meta[property=""og:price:amount""]');
                if (ogPrice) {
                    var pVal = ogPrice.getAttribute('content');
                    if (pVal && /^\d/.test(pVal)) price = pVal + ' TL';
                }
            }

            var minQty = '1';
            var bodyText = document.body.innerText || '';
            var mq = bodyText.match(/min\.?\s*sipari[sz]\s*(?:adedi)?[:\s]+(\d+)/i)
                  || bodyText.match(/minimum[:\s]+(\d+)\s*adet/i)
                  || bodyText.match(/en\s+az\s+(\d+)\s+adet/i)
                  || bodyText.match(/(\d+)\s*adet\s*(?:ve\s+)?[uü]zeri/i);
            if (mq) minQty = mq[1];

            var hasKdv = /\+\s*kdv/i.test(bodyText) ? '1' : '0';

            return [name, price, listPrice, minQty, hasKdv];
        }";

        var data = await page.EvaluateAsync<string[]?>(jsDetail);
        if (data == null || data.Length < 4 || string.IsNullOrWhiteSpace(data[0]))
            return rows;

        var productName = data[0];
        var priceText = data[1].Replace("₺", " TL").Trim();
        var listPriceText = data[2].Replace("₺", " TL").Trim();
        var minQtyStr = data[3];
        var hasKdvStr = data.Length > 4 ? data[4] : "0";

        var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
        var (lp, _, _, _, lvalid) = ScraperHelpers.ParsePrice(listPriceText);

        int.TryParse(minQtyStr, out var minQty);
        if (minQty < 1) minQty = 1;

        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        rows.Add(ScraperHelpers.CreateRow(
            store, seedUrl, pageUri.ToString(), category,
            productName,
            valid ? p : null,
            string.IsNullOrEmpty(ccy) ? "TRY" : ccy,
            !valid && !quote,
            kdv || hasKdvStr == "1",
            minQty,
            lvalid ? lp : null));

        return rows;
    }
}