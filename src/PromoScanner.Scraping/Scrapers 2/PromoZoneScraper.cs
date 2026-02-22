using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping.Scrapers;

public class PromoZoneScraper : ISiteScraper
{
    public string HostPattern => "promozone";

    public async Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl)
    {
        var path = pageUri.AbsolutePath.ToLowerInvariant();
        if (path.Contains("/urun/"))
            return await ExtractProductDetailAsync(page, pageUri, pageUri.Host, seedUrl);
        return await ExtractListingAsync(page, pageUri, pageUri.Host, seedUrl);
    }

    // ── Listeleme sayfası ─────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractListingAsync(
        IPage page, Uri pageUri, string store, string seedUrl)
    {
        var rows = new List<ResultRow>();
        var category = await ScraperHelpers.GetPageCategoryAsync(page);

        // Fiyatlar JS ile yükleniyor - ₺ sembolü görünene kadar bekle (max 6sn)
        await page.WaitForTimeoutAsync(1500);
        try
        {
            await page.WaitForFunctionAsync(
                @"() => document.body.innerText.indexOf('\u20ba') >= 0",
                null,
                new PageWaitForFunctionOptions { Timeout = 6000 });
        }
        catch { /* Fiyat yüklenmediyse devam et */ }

        // DOM yapısı: her ürün kartı bir container içinde.
        // a[href='/urun/'] ile ₺ elementi AYNI container'da ama a'nın SIBLING'i.
        // Strateji: her a linki için DOM ağacında yukarı çıkarak ₺ içeren container'ı bul.
        var jsListing = @"() => {
            var seen = {};
            var results = [];
            var links = document.querySelectorAll(""a[href*='/urun/']"");

            for (var i = 0; i < links.length; i++) {
                var a = links[i];
                var href = a.href || """";
                if (!href || seen[href]) continue;
                seen[href] = true;

                // Kart container'ını bul: a'dan yukarı çıkarak ₺ içeren ilk elementi bul
                // Max 6 seviye yukarı çıkıyoruz
                var card = null;
                var el = a;
                for (var lvl = 0; lvl < 6; lvl++) {
                    el = el.parentElement;
                    if (!el) break;
                    if ((el.innerText || """").indexOf(""\u20ba"") >= 0) {
                        card = el;
                        break;
                    }
                }
                // Hiç ₺ bulunamadıysa, a'nın 3 seviye üstünü kullan
                if (!card) {
                    card = a.parentElement;
                    if (card && card.parentElement) card = card.parentElement;
                    if (card && card.parentElement) card = card.parentElement;
                }

                // --- İsim ---
                var name = """";
                var titleAttr = a.getAttribute(""title"") || """";
                if (titleAttr.length > 0
                    && titleAttr.indexOf(""http"") < 0
                    && titleAttr.indexOf("".jpg"") < 0
                    && titleAttr.indexOf("".webp"") < 0
                    && titleAttr.indexOf(""/"") < 0) {
                    name = titleAttr.trim();
                }
                if (!name && card) {
                    var nameEl = card.querySelector("".product-title"")
                              || card.querySelector("".product-name"")
                              || card.querySelector("".urun-adi"")
                              || card.querySelector("".item-title"")
                              || card.querySelector(""h2"")
                              || card.querySelector(""h3"")
                              || card.querySelector(""h4"");
                    if (nameEl) {
                        var nt = (nameEl.innerText || """").replace(/\s+/g, "" "").trim();
                        if (nt.indexOf(""http"") < 0 && nt.indexOf("".jpg"") < 0 && nt.length > 2) name = nt;
                    }
                }
                if (!name) {
                    var innerTxt = (a.innerText || """").replace(/\s+/g, "" "").trim();
                    if (innerTxt.length > 2 && innerTxt.indexOf(""http"") < 0
                        && innerTxt.indexOf("".jpg"") < 0 && innerTxt.indexOf("".webp"") < 0) {
                        name = innerTxt;
                    }
                }
                if (!name) {
                    var slugMatch = href.match(/\/urun\/([^\/]+)/);
                    if (slugMatch) {
                        var slug = slugMatch[1].replace(/-pz\d+$/i, """").replace(/-[A-Z0-9]{5,}$/, """");
                        name = slug.replace(/-/g, "" "").trim();
                        if (name.length > 0) name = name.charAt(0).toUpperCase() + name.slice(1);
                    }
                }
                if (!name || name.length < 3) continue;

                // --- Fiyat: card içindeki ₺ leaf elementleri tara ---
                var price = """";
                var listPrice = """";
                var minQty = """";

                if (card) {
                    var allCardEls = card.querySelectorAll(""*"");
                    var netPriceEl = null;
                    var listPriceEl = null;

                    for (var k = 0; k < allCardEls.length; k++) {
                        var cel = allCardEls[k];
                        if (cel.children.length > 0) continue;
                        var ctxt = (cel.innerText || """").trim();
                        if (ctxt.indexOf(""\u20ba"") < 0 || ctxt.length > 20) continue;
                        var style = window.getComputedStyle(cel).textDecoration || """";
                        var tagN = cel.tagName.toLowerCase();
                        var isCrossed = style.indexOf(""line-through"") >= 0 || tagN === ""del"" || tagN === ""s"";
                        if (isCrossed) { if (!listPriceEl) listPriceEl = ctxt; }
                        else           { if (!netPriceEl)  netPriceEl  = ctxt; }
                    }
                    if (netPriceEl)  price     = netPriceEl;
                    if (listPriceEl) listPrice = listPriceEl;

                    // Min sipariş adeti
                    var cardText = (card.innerText || """");
                    var mqMatch = cardText.match(/min\.?\s*sipari[sz]\s*adedi[:\s]*(\d+)/i)
                               || cardText.match(/m[iI][nN]\.?\s+s[iI]par[iI][sz]\s+adedi[:\s]*(\d+)/i);
                    if (mqMatch) minQty = mqMatch[1];
                }

                results.push([href, name.substring(0, 150), price, listPrice, minQty]);
            }
            return results.slice(0, 200);
        }";

        var items = await page.EvaluateAsync<string[][]>(jsListing);

        foreach (var item in items ?? Array.Empty<string[]>())
        {
            if (item.Length < 2) continue;
            var href = item[0];
            var name = item[1];
            var priceText = (item.Length > 2 ? item[2] : "").Replace("\u20ba", " TL").Trim();
            var listPriceText = (item.Length > 3 ? item[3] : "").Replace("\u20ba", " TL").Trim();
            var minQtyStr = item.Length > 4 ? item[4] : "";

            if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;
            if (ScraperHelpers.IsNavigationLink(href, name)) continue;

            var (p, ccy, quote, kdv, valid) = ScraperHelpers.ParsePrice(priceText);
            var (lp, _, _, _, lvalid) = ScraperHelpers.ParsePrice(listPriceText);

            int.TryParse(minQtyStr, out var minQty);
            if (minQty < 1) minQty = 1;

            rows.Add(ScraperHelpers.CreateRow(
                store, seedUrl, href, category, name,
                valid ? p : null,
                string.IsNullOrEmpty(ccy) ? "TRY" : ccy,
                !valid,
                kdv,
                minQty,
                lvalid ? lp : null));
        }

        return rows;
    }

    // ── Ürün detay sayfası ────────────────────────────────────────────────────
    private async Task<List<ResultRow>> ExtractProductDetailAsync(
        IPage page, Uri pageUri, string store, string seedUrl)
    {
        var rows = new List<ResultRow>();

        // ₺ sembolü görünene kadar bekle (max 8sn) — h1'den daha güvenilir
        await page.WaitForTimeoutAsync(1000);
        try
        {
            await page.WaitForFunctionAsync(
                @"() => document.body.innerText.indexOf('\u20ba') >= 0",
                null,
                new PageWaitForFunctionOptions { Timeout = 8000 });
        }
        catch { /* Fiyat yüklenmediyse devam et */ }

        var jsDetail = @"() => {
            // --- Ürün adı ---
            var name = """";
            var h1 = document.querySelector(""h1"");
            if (h1) {
                var h1Text = (h1.innerText || """").trim();
                if (h1Text.length > 3 && h1Text.indexOf(""http"") < 0
                    && h1Text.indexOf("".jpg"") < 0 && h1Text.indexOf("".webp"") < 0) {
                    name = h1Text;
                }
            }
            if (!name) {
                var og = document.querySelector(""meta[property='og:title']"");
                if (og) name = (og.getAttribute(""content"") || """").trim();
            }
            if (!name) {
                var titleTag = document.querySelector(""title"");
                if (titleTag) {
                    var t = (titleTag.innerText || """").split(""|"")[0].trim();
                    if (t.length > 3) name = t;
                }
            }
            if (!name) return ["""", """", """", ""1"", ""0""];

            // --- Fiyat: ₺ leaf elementleri ---
            var price = """";
            var listPrice = """";
            var allEls = document.querySelectorAll(""*"");
            for (var i = 0; i < allEls.length; i++) {
                var el = allEls[i];
                if (el.children.length > 0) continue;
                var txt = (el.innerText || """").trim();
                if (txt.indexOf(""\u20ba"") < 0 || txt.length > 25) continue;
                var style = window.getComputedStyle(el).textDecoration || """";
                var tag = el.tagName.toLowerCase();
                var crossed = style.indexOf(""line-through"") >= 0 || tag === ""del"" || tag === ""s"";
                if (crossed) { if (!listPrice) listPrice = txt; }
                else         { if (!price)     price     = txt; }
            }

            // Min sipariş
            var minQty = ""1"";
            var bodyText = document.body.innerText || """";
            var minMatch = bodyText.match(/min\.?\s*sipari[sz]\s*adedi[:\s]+(\d+)/i)
                        || bodyText.match(/en\s+az\s+(\d+)\s+adet/i);
            if (minMatch) minQty = minMatch[1];

            var hasKdv = /\+\s*kdv/i.test(bodyText) ? ""1"" : ""0"";
            return [name, price, listPrice, minQty, hasKdv];
        }";

        var data = await page.EvaluateAsync<string[]>(jsDetail);
        if (data == null || data.Length < 4 || string.IsNullOrWhiteSpace(data[0]))
            return rows;

        var productName = data[0];
        var priceText = data[1].Replace("\u20ba", " TL").Trim();
        var listPriceText = data[2].Replace("\u20ba", " TL").Trim();
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