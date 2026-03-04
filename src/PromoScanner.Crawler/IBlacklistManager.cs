namespace PromoScanner.Crawler;

/// <summary>
/// Quote blacklist yönetim arayüzü.
/// </summary>
public interface IBlacklistManager
{
    bool IsBlacklisted(string url);
    void AddConfirmed(string url);
    void Save();
    int Count { get; }
    int NewCount { get; }
    IReadOnlySet<string> AllUrls { get; }
}
