using Microsoft.Playwright;
using PromoScanner.Core;

namespace PromoScanner.Scraping;

public interface ISiteScraper
{
    /// <summary>Bu scraper'ın hangi host'u tanıdığı. Örn: "bidolubaski"</summary>
    string HostPattern { get; }

    Task<List<ResultRow>> ExtractAsync(IPage page, Uri pageUri, string seedUrl);
}