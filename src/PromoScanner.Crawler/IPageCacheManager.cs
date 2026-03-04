using PromoScanner.Core;

namespace PromoScanner.Crawler;

/// <summary>
/// Sayfa cache yönetim arayüzü.
/// </summary>
public interface IPageCacheManager
{
    bool Contains(string url);
    PageCacheEntry? TryGet(string url);
    void Set(PageCacheEntry entry);
    IReadOnlyList<PageCacheEntry> GetRefreshCandidates(HashSet<string> visited, IBlacklistManager blacklist);
    void Save();
    int Count { get; }
    int ProductPageCount { get; }
}
