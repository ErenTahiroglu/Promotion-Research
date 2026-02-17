using PromoScanner.Scraping.Scrapers;

namespace PromoScanner.Scraping;

public class ScraperRegistry
{
    private readonly List<ISiteScraper> _scrapers;

    public ScraperRegistry()
    {
        // ✅ YENİ SİTE EKLEMEK İÇİN SADECE BURAYA BİR SATIR EKLE:
        _scrapers = new List<ISiteScraper>
        {
            new BidoluBaskiScraper(),
            new PromoZoneScraper(),
            new TurkuazPromosyonScraper(),
            new BatiPromosyonScraper(),
            new TekinozalitScraper(),
            new PromosyonikScraper(),
            new AksiyonPromosyonScraper(),
            new IlpenScraper(),
        };
    }

    /// <summary>URI'ye göre uygun scraper'ı döner. Bulunamazsa null.</summary>
    public ISiteScraper? FindScraper(Uri uri)
        => _scrapers.FirstOrDefault(s =>
               uri.Host.Contains(s.HostPattern, StringComparison.OrdinalIgnoreCase));
}