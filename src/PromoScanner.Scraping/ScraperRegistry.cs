using PromoScanner.Scraping.Scrapers;

namespace PromoScanner.Scraping;

public sealed class ScraperRegistry : IScraperRegistry
{
    private readonly List<ISiteScraper> _scrapers;

    public ScraperRegistry()
    {
        // ✅ YENİ SİTE EKLEMEK İÇİN SADECE BURAYA BİR SATIR EKLE:
        _scrapers =
        [
            new BidoluBaskiScraper(),
            new PromoZoneScraper(),
            new TurkuazPromosyonScraper(),
            new BatiPromosyonScraper(),
            new TekinozalitScraper(),
            new PromosyonikScraper(),
            new AksiyonPromosyonScraper(),
            new IlpenScraper(),
        ];
    }

    /// <summary>
    /// DI üzerinden scraper listesi alarak oluşturma imkânı.
    /// </summary>
    public ScraperRegistry(IEnumerable<ISiteScraper> scrapers)
    {
        _scrapers = scrapers.ToList();
    }

    /// <summary>
    /// URI'ye göre uygun scraper'ı döner. Domain sınırında (. veya başlangıç) eşleşme yapar.
    /// Böylece "promozone" sadece "promozone.com.tr" ile eşleşir, "notpromozone.com" ile değil.
    /// </summary>
    public ISiteScraper? FindScraper(Uri uri)
    {
        var host = uri.Host;
        return _scrapers.FirstOrDefault(s =>
        {
            var pattern = s.HostPattern;
            int idx = host.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            // Pattern domain sınırında olmalı: ya host başındaysa ya da öncesinde '.' olmalı
            return idx == 0 || host[idx - 1] == '.';
        });
    }
}