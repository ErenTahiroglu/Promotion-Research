using ClosedXML.Excel;
using PromoScanner.Core;

namespace PromoScanner.Reporting;

/// <summary>
/// Excel rapor yazıcısı. Özet, Tüm Ürünler, Karşılaştırma, En İyi Fırsatlar, Teklif Gereken ve Teklif Alternatifleri sayfaları oluşturur.
/// </summary>
public static class ExcelReportWriter
{
    public static void Write(string filePath, IReadOnlyList<ResultRow> valid, IReadOnlyList<ResultRow> priced,
        IReadOnlyList<ResultRow> quote, IReadOnlyList<SmartProductGroup> groups, IReadOnlyList<QuoteAlternative> quoteAlternatives)
    {
        using var wb = new XLWorkbook();
        var hBg = XLColor.FromHtml("#1F4E79");
        var alt = XLColor.FromHtml("#EBF3FB");
        var deal = XLColor.FromHtml("#E2EFDA");
        var warn = XLColor.FromHtml("#FFF2CC");
        var qBg = XLColor.FromHtml("#FCE4D6");

        // Özet
        {
            var ws = wb.Worksheets.Add("Özet");
            ws.Cell("B2").Value = "PromoScanner - Tarama Özeti";
            ws.Cell("B2").Style.Font.FontSize = 20; ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("B2").Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
            ws.Range("B2:G2").Merge();
            ws.Cell("B3").Value = $"Oluşturulma: {DateTimeOffset.Now:dd.MM.yyyy HH:mm}";
            ws.Cell("B3").Style.Font.Italic = true; ws.Cell("B3").Style.Font.FontColor = XLColor.Gray;
            int r = 5;
            ws.Cell(r, 2).Value = "Metrik"; ws.Cell(r, 3).Value = "Değer"; ws.Row(r).Style.Font.Bold = true;

            (string Label, int Value)[] st =
            [
                ("Geçerli Ürün", valid.Count),
                ("Fiyatlı Ürün", priced.Count),
                ("Teklif Gereken", quote.Count),
                ("Teklif Alternatifi", quoteAlternatives.Count),
                ("Karşılaştırma Grubu", groups.Count),
                ("2+ Site Eşleşme", groups.Count(g => g.SiteCount >= 2))
            ];
            for (int i = 0; i < st.Length; i++)
            {
                ws.Cell(r + 1 + i, 2).Value = st[i].Label;
                ws.Cell(r + 1 + i, 3).Value = st[i].Value;
                ws.Cell(r + 1 + i, 3).Style.Font.Bold = true;
            }
            ws.Cell(r, 5).Value = "Site"; ws.Cell(r, 6).Value = "Ürün"; ws.Cell(r, 7).Value = "Fiyatlı";

            var ss = valid.GroupBy(p => p.Store)
                .Select(g => (Store: g.Key, Total: g.Count(), Priced: g.Count(x => x.Price > 0)))
                .OrderByDescending(x => x.Total)
                .ToList();
            for (int i = 0; i < ss.Count; i++)
            {
                ws.Cell(r + 1 + i, 5).Value = ss[i].Store;
                ws.Cell(r + 1 + i, 6).Value = ss[i].Total;
                ws.Cell(r + 1 + i, 7).Value = ss[i].Priced;
            }
            ws.Columns().AdjustToContents();
        }

        // Tüm Ürünler
        {
            var ws = wb.Worksheets.Add("Tüm Ürünler"); ws.SheetView.FreezeRows(1);
            string[] h = ["Mağaza", "Kategori", "Ürün", "Fiyat", "Liste Fiyat", "PB", "KDV", "Min.Sip.", "URL", "Zaman"];
            Hdr(ws, 1, h, hBg);
            for (int i = 0; i < priced.Count; i++)
            {
                var rw = priced[i]; int r = i + 2;
                ws.Cell(r, 1).Value = rw.Store; ws.Cell(r, 2).Value = rw.Category; ws.Cell(r, 3).Value = rw.ProductName;
                ws.Cell(r, 4).Value = rw.Price.HasValue ? (double)rw.Price.Value : 0;
                ws.Cell(r, 5).Value = rw.ListPrice.HasValue ? (double)rw.ListPrice.Value : 0;
                ws.Cell(r, 6).Value = rw.Currency;
                ws.Cell(r, 7).Value = rw.HasKDV ? "Evet" : "Hayır"; ws.Cell(r, 8).Value = rw.MinOrderQty;
                ws.Cell(r, 9).Value = rw.Url; ws.Cell(r, 10).Value = rw.Timestamp.ToString("dd.MM.yyyy HH:mm");
                ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
                if (Uri.TryCreate(rw.Url, UriKind.Absolute, out var u)) ws.Cell(r, 9).SetHyperlink(new XLHyperlink(u.ToString()));
                if (i % 2 == 1) RowBg(ws, r, h.Length, alt);
            }
            Tbl(ws, 1, priced.Count + 1, h.Length); ws.Columns().AdjustToContents(); ws.Column(9).Width = 50;
        }

        // Karşılaştırma
        {
            var ws = wb.Worksheets.Add("Karşılaştırma"); ws.SheetView.FreezeRows(1);
            string[] h =
            [
                "Kategori","Kapasite","Özellik","#","Site#",
                "Min Fiyat","Min Site","Min Adet","Min Toplam",
                "Max Fiyat","Max Site","Max Adet","Max Toplam",
                "Birim Fark","Fark%","Ort","Site Maliyet Detayı","Ürünler","Siteler","Min URL"
            ];
            Hdr(ws, 1, h, hBg);
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i]; int r = i + 2;
                double pct = (g.MinPrice.HasValue && g.MinPrice.Value > 0 && g.PriceDifference.HasValue)
                    ? (double)(g.PriceDifference.Value / g.MinPrice.Value) : 0;
                
                ws.Cell(r, 1).Value = g.Category; ws.Cell(r, 2).Value = g.Capacity; ws.Cell(r, 3).Value = g.KeyFeatures;
                ws.Cell(r, 4).Value = g.ProductCount; ws.Cell(r, 5).Value = g.SiteCount;
                ws.Cell(r, 6).Value = g.MinPrice.HasValue ? (double)g.MinPrice.Value : 0;
                ws.Cell(r, 7).Value = g.MinPriceStore;
                ws.Cell(r, 8).Value = g.MinPriceMinQty;
                ws.Cell(r, 9).Value = g.MinPriceTotalCost.HasValue ? (double)g.MinPriceTotalCost.Value : 0;
                ws.Cell(r, 10).Value = g.MaxPrice.HasValue ? (double)g.MaxPrice.Value : 0;
                ws.Cell(r, 11).Value = g.MaxPriceStore;
                ws.Cell(r, 12).Value = g.MaxPriceMinQty;
                ws.Cell(r, 13).Value = g.MaxPriceTotalCost.HasValue ? (double)g.MaxPriceTotalCost.Value : 0;
                ws.Cell(r, 14).Value = g.PriceDifference.HasValue ? (double)g.PriceDifference.Value : 0;
                ws.Cell(r, 15).Value = pct;
                ws.Cell(r, 16).Value = g.AvgPrice.HasValue ? (double)g.AvgPrice.Value : 0;
                ws.Cell(r, 17).Value = g.SiteCostBreakdown;
                ws.Cell(r, 18).Value = g.AllProductNames; ws.Cell(r, 19).Value = g.AllStores;
                ws.Cell(r, 20).Value = g.MinPriceUrl;
                foreach (int c in (int[])[6, 9, 10, 13, 14, 16]) ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(r, 15).Style.NumberFormat.Format = "0.0\"%\"";

                if (Uri.TryCreate(g.MinPriceUrl, UriKind.Absolute, out var u)) ws.Cell(r, 20).SetHyperlink(new XLHyperlink(u.ToString()));
                
                // %50'den fazla fark varsa sarı (uyarı)
                if (g.SiteCount >= 2 && pct > 0.50) RowBg(ws, r, h.Length, warn);
                else if (g.SiteCount >= 2) RowBg(ws, r, h.Length, deal);
                else if (i % 2 == 1) RowBg(ws, r, h.Length, alt);
            }
            Tbl(ws, 1, groups.Count + 1, h.Length); ws.Columns().AdjustToContents();
            ws.Column(17).Width = 80; ws.Column(18).Width = 60; ws.Column(20).Width = 50;
        }

        // En İyi Fırsatlar (Toplam Fiyat Farkına Göre Sıralı)
        {
            var ds = groups.Where(g => g.SiteCount >= 2 && g.PriceDifference > 0)
                .OrderByDescending(g => (g.MaxPriceTotalCost ?? 0) - (g.MinPriceTotalCost ?? 0))
                .Take(50).ToList();
            var ws = wb.Worksheets.Add("En İyi Fırsatlar"); ws.SheetView.FreezeRows(1);
            string[] h =
            [
                "#","Kategori","Kapasite",
                "Min Fiyat","Min Site","Min Adet","Min Toplam",
                "Max Fiyat","Max Site","Max Adet","Max Toplam",
                "Birim Fark","Toplam Fark","Site#","Maliyet Detayı","URL"
            ];
            Hdr(ws, 1, h, XLColor.FromHtml("#C00000"));
            for (int i = 0; i < ds.Count; i++)
            {
                var g = ds[i]; int r = i + 2;
                double totalDiff = (double)((g.MaxPriceTotalCost ?? 0) - (g.MinPriceTotalCost ?? 0));
                
                ws.Cell(r, 1).Value = i + 1; ws.Cell(r, 2).Value = g.Category; ws.Cell(r, 3).Value = g.Capacity;
                ws.Cell(r, 4).Value = g.MinPrice.HasValue ? (double)g.MinPrice.Value : 0;
                ws.Cell(r, 5).Value = g.MinPriceStore;
                ws.Cell(r, 6).Value = g.MinPriceMinQty;
                ws.Cell(r, 7).Value = g.MinPriceTotalCost.HasValue ? (double)g.MinPriceTotalCost.Value : 0;
                ws.Cell(r, 8).Value = g.MaxPrice.HasValue ? (double)g.MaxPrice.Value : 0;
                ws.Cell(r, 9).Value = g.MaxPriceStore;
                ws.Cell(r, 10).Value = g.MaxPriceMinQty;
                ws.Cell(r, 11).Value = g.MaxPriceTotalCost.HasValue ? (double)g.MaxPriceTotalCost.Value : 0;
                ws.Cell(r, 12).Value = g.PriceDifference.HasValue ? (double)g.PriceDifference.Value : 0;
                ws.Cell(r, 13).Value = totalDiff; ws.Cell(r, 14).Value = g.SiteCount;
                ws.Cell(r, 15).Value = g.SiteCostBreakdown;
                ws.Cell(r, 16).Value = g.MinPriceUrl;
                foreach (int c in (int[])[4, 7, 8, 11, 12, 13]) ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
                
                if (Uri.TryCreate(g.MinPriceUrl, UriKind.Absolute, out var u)) ws.Cell(r, 16).SetHyperlink(new XLHyperlink(u.ToString()));
                if (i < 10) RowBg(ws, r, h.Length, deal); else if (i % 2 == 1) RowBg(ws, r, h.Length, alt);
            }
            Tbl(ws, 1, ds.Count + 1, h.Length); ws.Columns().AdjustToContents();
            ws.Column(15).Width = 80; ws.Column(16).Width = 50;
        }

        // Teklif Gereken
        if (quote.Count > 0)
        {
            var ws = wb.Worksheets.Add("Teklif Gereken"); ws.SheetView.FreezeRows(1);
            string[] h = ["Mağaza", "Kategori", "Ürün", "Min.Sip.", "URL", "Zaman"];
            Hdr(ws, 1, h, XLColor.FromHtml("#ED7D31"));
            for (int i = 0; i < quote.Count; i++)
            {
                var rw = quote[i]; int r = i + 2;
                ws.Cell(r, 1).Value = rw.Store; ws.Cell(r, 2).Value = rw.Category; ws.Cell(r, 3).Value = rw.ProductName;
                ws.Cell(r, 4).Value = rw.MinOrderQty; ws.Cell(r, 5).Value = rw.Url;
                ws.Cell(r, 6).Value = rw.Timestamp.ToString("dd.MM.yyyy HH:mm");
                if (Uri.TryCreate(rw.Url, UriKind.Absolute, out var u)) ws.Cell(r, 5).SetHyperlink(new XLHyperlink(u.ToString()));
                if (i % 2 == 1) RowBg(ws, r, h.Length, qBg);
            }
            Tbl(ws, 1, quote.Count + 1, h.Length); ws.Columns().AdjustToContents();
        }

        // Teklif Alternatifleri
        if (quoteAlternatives.Count > 0)
        {
            var ws = wb.Worksheets.Add("Teklif Alternatifleri"); ws.SheetView.FreezeRows(1);
            string[] h = [
                "Teklif İsteyen Site", "Ürün Adı", "Teklif Linki",
                "Alternatif Site", "Alternatif Fiyat", "Alternatif Link"
            ];
            Hdr(ws, 1, h, XLColor.FromHtml("#7030A0")); // Mor başlık
            for (int i = 0; i < quoteAlternatives.Count; i++)
            {
                var qa = quoteAlternatives[i]; int r = i + 2;
                ws.Cell(r, 1).Value = qa.QuoteStore; ws.Cell(r, 2).Value = qa.QuoteProductName; 
                ws.Cell(r, 3).Value = qa.QuoteUrl;
                if (Uri.TryCreate(qa.QuoteUrl, UriKind.Absolute, out var qu)) ws.Cell(r, 3).SetHyperlink(new XLHyperlink(qu.ToString()));

                ws.Cell(r, 4).Value = qa.AlternativeStore; 
                ws.Cell(r, 5).Value = (double)qa.AlternativePrice;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
                
                ws.Cell(r, 6).Value = qa.AlternativeUrl;
                if (Uri.TryCreate(qa.AlternativeUrl, UriKind.Absolute, out var au)) ws.Cell(r, 6).SetHyperlink(new XLHyperlink(au.ToString()));

                if (i % 2 == 1) RowBg(ws, r, h.Length, XLColor.FromHtml("#F2E6FA"));
            }
            Tbl(ws, 1, quoteAlternatives.Count + 1, h.Length); ws.Columns().AdjustToContents();
            ws.Column(3).Width = 50; ws.Column(6).Width = 50;
        }

        wb.SaveAs(filePath);
    }

    private static void Hdr(IXLWorksheet ws, int row, string[] h, XLColor bg)
    {
        for (int c = 0; c < h.Length; c++)
        {
            var cl = ws.Cell(row, c + 1); cl.Value = h[c]; cl.Style.Font.Bold = true;
            cl.Style.Font.FontColor = XLColor.White; cl.Style.Fill.BackgroundColor = bg;
            cl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void RowBg(IXLWorksheet ws, int row, int cols, XLColor c)
        => ws.Range(row, 1, row, cols).Style.Fill.BackgroundColor = c;

    private static void Tbl(IXLWorksheet ws, int r1, int r2, int c2)
    {
        if (r2 <= r1) return;
        var rng = ws.Range(r1, 1, r2, c2); rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        rng.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        ws.Range(r1, 1, r1, c2).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
    }
}
