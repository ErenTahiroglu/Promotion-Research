namespace PromoScanner.Scraping;

/// <summary>
/// Site scraper kayıt arayüzü — DI üzerinden enjekte edilir.
/// </summary>
public interface IScraperRegistry
{
    /// <summary>URI'ye göre uygun scraper'ı döner. Bulunamazsa null.</summary>
    ISiteScraper? FindScraper(Uri uri);
}
