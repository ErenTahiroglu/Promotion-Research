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
        var path = pageUri.AbsolutePath.ToLowerInvariant();

        // Urun detay sayfasi: /urun/ iceriyor
        if (path.Contains("/urun/"))
            return await ExtractProductDetailAsync(page, pageUri, store, seedUrl);

        // Kategori/listeleme sayfasi
        return await ExtractListingAsync(page, pageUri, store, seedUrl);
    }

    // ── Listeleme sayfasi ─────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractListingAsync(
        IPage page, Uri pageUri, string store, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        try { await page.WaitForSelectorAsync("a[href*='/urun/']", new() { Timeout = 6000 }); } catch { }

        // JavaScript ile tum urun kartlarini topla
        var items = await page.EvaluateAsync<string[][]>(@"() => {
            const seen = new Set();
            const results = [];
            document.querySelectorAll('a[href*=""/urun/""]').forEach(a => {
                const href = a.href || '';
                if (!href.includes('/urun/') || seen.has(href)) return;
                seen.add(href);

                const card = a.closest('li, article, [class*=""product""], [class*=""item""], [class*=""card""]')
                          || a.parentElement;

                // Isim
                let name = a.getAttribute('title') || (a.innerText||'').replace(/\s+/g,' ').trim();
                if (!name && card) {
                    const el = card.querySelector('h2,h3,h4,p,[class*=""name""],[class*=""title""]');
                    name = el ? (el.innerText||'').replace(/\s+/g,' ').trim() : '';
                }

                // Fiyat - listing sayfasinda gozukuyorsa al
                let price = '';
                if (card) {
                    const priceEl = card.querySelector('[class*=""price""], [class*=""fiyat""]');
                    price = priceEl ? (priceEl.innerText||'').replace(/\s+/g,' ').trim() : '';
                }

                if (name.length > 2) results.push([href, name.slice(0,150), price]);
            });
            return results.slice(0, 200);
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

            // Fiyat listing'de yoksa teklif olarak isaretle — detay sayfasi ziyaretinde guncellenir
            rows.Add(ScraperHelpers.CreateRow(store, seedUrl, href, category, name,
                valid ? p : null, ccy, !valid, kdv));
        }
        return rows;
    }

    // ── Urun detay sayfasi ────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractProductDetailAsync(
        IPage page, Uri pageUri, string store, string seedUrl)
    {
        var rows = new List<ResultRow>();

        try { await page.WaitForSelectorAsync("h1", new() { Timeout = 6000 }); } catch { }

        var data = await page.EvaluateAsync<string[]>(@"() => {
            // Urun adi
            const h1 = document.querySelector('h1');
            const name = h1 ? h1.innerText.trim() : '';

            // Fiyat Araligi (adet fiyati) - 'Fiyat Araligi' yakinindaki fiyat
            let price = '';
            let listPrice = '';
            let minQty = '1';

            // Tum metin bloklarini tara
            document.querySelectorAll('[class*=""price""], [class*=""fiyat""]').forEach(el => {
                const cls = el.className.toLowerCase();
                const txt = (el.innerText || '').trim();
                if (!txt || txt.length > 50) return;
                if (/\d+[,\.]\d+/.test(txt) || /\d+\s*TL/.test(txt)) {
                    if (!price) price = txt;
                    else if (!listPrice) listPrice = txt;
                }
            });

            // 'Fiyat Araligi' ve 'Liste Fiyati' labellarini bul
            document.querySelectorAll('*').forEach(el => {
                const txt = (el.innerText || '').trim();
                if (txt === 'Fiyat Araligi' || txt === 'Fiyat Aralığı') {
                    const next = el.nextElementSibling || el.parentElement?.querySelector('[class*=""price""]');
                    if (next) price = (next.innerText||'').replace(/[^\d,\.]/g,'').trim() + ' TL';
                }
                if (txt === 'Liste Fiyati' || txt === 'Liste Fiyatı') {
                    const next = el.nextElementSibling || el.parentElement?.querySelector('[class*=""price""]');
                    if (next) listPrice = (next.innerText||'').replace(/[^\d,\.]/g,'').trim() + ' TL';
                }
            });

            // Minimum Siparis Adeti
            document.querySelectorAll('*').forEach(el => {
                const txt = (el.innerText || '').trim();
                const m = txt.match(/Minimum\s+Sipari[sş]\s+Adeti[:\s]+(\d+)/i);
                if (m) minQty = m[1];
            });

            // KDV
            const bodyText = document.body.innerText || '';
            const hasKdv = bodyText.includes('+KDV') || bodyText.includes('+ KDV');

            return [name, price, listPrice, minQty, hasKdv ? '1' : '0'];
        }");

        if (data == null || data.Length < 4) return rows;

        var productName = data[0];
        var priceText = data[1];
        var listPriceText = data[2];
        var minQtyStr = data[3];
        var hasKdvStr = data.Length > 4 ? data[4] : "0";

        if (string.IsNullOrWhiteSpace(productName)) return rows;

        var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
        var (lp, _, _, _, lvalid) = ScraperHelpers.ParsePrice(listPriceText);

        int.TryParse(minQtyStr, out var minQty);
        if (minQty < 1) minQty = 1;
        bool hasKdv = hasKdvStr == "1";

        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        rows.Add(ScraperHelpers.CreateRow(
            store, seedUrl, pageUri.ToString(), category,
            productName,
            valid ? p : null,
            string.IsNullOrEmpty(ccy) ? "TRY" : ccy,
            !valid && !quote,
            kdv || hasKdv,
            minQty,
            lvalid ? lp : null
        ));

        return rows;
    }
}