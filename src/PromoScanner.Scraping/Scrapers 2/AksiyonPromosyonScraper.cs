using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class AksiyonPromosyonScraper : ISiteScraper
{
    public string HostPattern => "aksiyonpromosyon";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        // Detay sayfası mı yoksa listeleme mi?
        if (IsDetailPage(pageUri))
            return await ExtractDetailAsync(page, pageUri, seedUrl);

        return await ExtractListingAsync(page, pageUri, seedUrl);
    }

    // ── URL pattern tespiti ───────────────────────────────────────────────────
    /// <summary>
    /// Aksiyon ürün detay URL'leri: /urun-adi-sonunda-sayı  (ör: ...-2595)
    /// Listeleme/ana sayfa ise kısa path.
    /// </summary>
    private static bool IsDetailPage(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        // Ana sayfa veya çok kısa path → listeleme
        if (string.IsNullOrEmpty(path) || path.Split('/').Length < 1) return false;
        // Detay URL'ler genelde uzun slug + sonunda sayı: /baret-seklinde-...-2595
        return Regex.IsMatch(path, @"-\d{2,}$");
    }

    // ── Listeleme sayfası ─────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractListingAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".product-item", new() { Timeout = 5000 }); } catch { }

        foreach (var item in await page.QuerySelectorAllAsync(".product-item"))
        {
            var nameEl = await item.QuerySelectorAsync("a.cat, .title a");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await item.QuerySelectorAsync(".get-price.price, .price.net");
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

    // ── Ürün detay sayfası ────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractDetailAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;

        await page.WaitForTimeoutAsync(1000);

        // Fiyat yüklenmesini bekle
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
        catch { }

        var jsDetail = @"() => {
            // ── Ürün adı ──────────────────────────────────────────────
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

            // ── Fiyat ──────────────────────────────────────────────────
            var price = '';
            var listPrice = '';

            // Bilinen price elementleri
            var priceSelectors = [
                '.get-price.price', '.price.net', '.product-price',
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

            // Fallback: leaf elementlerde ₺/TL ara
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

            // og:price meta
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
                  || bodyText.match(/en\s+az\s+(\d+)\s+adet/i);
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