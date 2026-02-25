using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class TurkuazPromosyonScraper : ISiteScraper
{
    public string HostPattern => "turkuazpromosyon";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        // Detay sayfası mı yoksa listeleme mi?
        if (IsDetailPage(pageUri))
            return await ExtractDetailAsync(page, pageUri, seedUrl);

        return await ExtractListingAsync(page, pageUri, seedUrl);
    }

    // ── URL pattern tespiti ───────────────────────────────────────────────────
    /// <summary>
    /// Turkuaz ürün detay URL'leri: /kategori/ÜRÜNKODU şeklinde.
    /// Örn: /promosyon-ajandalar/6240FUM   /ikili-kalem-setleri/5870FUM
    /// Listeleme URL'leri: /promosyon-ajandalar  /promosyon-kalem
    /// </summary>
    private static bool IsDetailPage(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) return false;
        var last = segments.Last();
        // Ürün kodları: 4+ rakam + 0-4 harf (6240FUM, 5870FUM, 4990SYH, 2260)
        return Regex.IsMatch(last, @"^\d{3,}[A-Za-z]{0,5}$");
    }

    // ── Listeleme sayfası ─────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractListingAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var store = pageUri.Host;
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync(".left, .product-block", new() { Timeout = 5000 }); } catch { }

        foreach (var item in await page.QuerySelectorAllAsync(".left, .product-block"))
        {
            var nameEl = await item.QuerySelectorAsync("h3.name a, .name a");
            if (nameEl == null) continue;

            var name = (await nameEl.InnerTextAsync())?.Trim();
            var href = await nameEl.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name)) continue;

            var priceEl = await item.QuerySelectorAsync(".price-gruop, .price .special-price");
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

        // Sayfanın yüklenmesini bekle
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
        catch { /* Fiyat olmayabilir */ }

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

            // Önce bilinen price elementlerini dene
            var priceSelectors = [
                '.price .special-price', '.price-gruop .special-price',
                '.special-price', '.product-price',
                '.price .new-price', '.price-new',
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

            // Bulunamadıysa: sayfadaki tüm leaf elementlerde ₺/TL ara
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

            // ── Kategori ───────────────────────────────────────────────
            var category = '';
            var breadcrumb = document.querySelector('.breadcrumb, nav[aria-label=""breadcrumb""]');
            if (breadcrumb) {
                var links = breadcrumb.querySelectorAll('a');
                if (links.length >= 2) {
                    category = (links[links.length - 1].innerText || '').trim();
                }
            }
            if (!category) category = '';

            // ── Min sipariş ────────────────────────────────────────────
            var minQty = '1';
            var bodyText = document.body.innerText || '';
            var mq = bodyText.match(/min\.?\s*sipari[sz]\s*(?:adedi)?[:\s]+(\d+)/i)
                  || bodyText.match(/minimum[:\s]+(\d+)\s*adet/i)
                  || bodyText.match(/en\s+az\s+(\d+)\s+adet/i);
            if (mq) minQty = mq[1];

            var hasKdv = /\+\s*kdv/i.test(bodyText) ? '1' : '0';

            return [name, price, listPrice, category, minQty, hasKdv];
        }";

        var data = await page.EvaluateAsync<string[]?>(jsDetail);
        if (data == null || data.Length < 4 || string.IsNullOrWhiteSpace(data[0]))
            return rows;

        var productName = data[0];
        var priceText = data[1].Replace("₺", " TL").Trim();
        var listPriceText = data[2].Replace("₺", " TL").Trim();
        var detailCategory = data[3];
        var minQtyStr = data[4];
        var hasKdvStr = data.Length > 5 ? data[5] : "0";

        var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
        var (lp, _, _, _, lvalid) = ScraperHelpers.ParsePrice(listPriceText);

        int.TryParse(minQtyStr, out var minQty);
        if (minQty < 1) minQty = 1;

        var category = !string.IsNullOrWhiteSpace(detailCategory)
            ? detailCategory
            : await ScraperHelpers.GetPageCategoryAsync(page);

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