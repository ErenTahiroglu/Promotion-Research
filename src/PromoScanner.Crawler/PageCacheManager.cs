using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PromoScanner.Core;

namespace PromoScanner.Crawler;

/// <summary>
/// page_cache.csv dosyasını yöneten sınıf.
/// Daha önce ziyaret edilen sayfaları ve ürün bilgilerini tutar.
/// Atomic write ile veri kaybını önler.
/// </summary>
public sealed class PageCacheManager : IPageCacheManager
{
    private readonly string _filePath;
    private readonly Dictionary<string, PageCacheEntry> _cache;
    private readonly ILogger<PageCacheManager> _logger;

    public PageCacheManager(string dataDir, ILogger<PageCacheManager> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(dataDir, "page_cache.csv");
        _cache = new Dictionary<string, PageCacheEntry>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(_filePath))
        {
            foreach (var line in File.ReadAllLines(_filePath).Skip(1))
            {
                // Güvenli parse: URL içinde ; olabilir → sağdan parse et
                var parts = line.Split(';');
                if (parts.Length < 4 || string.IsNullOrWhiteSpace(parts[0])) continue;

                // 5 sütunlu format: URL;Store;HasProducts;ProductCount;LastVisited
                // 4 sütunlu eski format: URL;Store;HasProducts;ProductCount
                var url = parts[0].Trim();
                var store = parts[1].Trim();
                var hasProducts = parts[2].Trim() == "1";
                int.TryParse(parts[3].Trim(), out var pc);

                DateTimeOffset lastVisited = DateTimeOffset.MinValue;
                if (parts.Length >= 5 && DateTimeOffset.TryParse(parts[4].Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var lv))
                    lastVisited = lv;

                _cache[url] = new PageCacheEntry
                {
                    Url = url,
                    Store = store,
                    HasProducts = hasProducts,
                    ProductCount = pc,
                    LastVisited = lastVisited
                };
            }
            _logger.LogInformation("Cache yüklendi: {Count} sayfa ({ProductPages} ürünlü)",
                _cache.Count, _cache.Count(kv => kv.Value.HasProducts));
        }
    }

    public bool Contains(string url) => _cache.ContainsKey(url);

    public PageCacheEntry? TryGet(string url) =>
        _cache.TryGetValue(url, out var entry) ? entry : null;

    public void Set(PageCacheEntry entry) => _cache[entry.Url] = entry;

    public int Count => _cache.Count;
    public int ProductPageCount => _cache.Count(kv => kv.Value.HasProducts);

    public IReadOnlyList<PageCacheEntry> GetRefreshCandidates(
        HashSet<string> visited, IBlacklistManager blacklist)
    {
        return _cache.Values
            .Where(c => c.HasProducts && c.ProductCount > 0)
            .Where(c => !visited.Contains(c.Url) && !blacklist.IsBlacklisted(c.Url))
            .OrderBy(c => c.LastVisited)          // En eski sayfalar önce
            .ThenByDescending(c => c.ProductCount)
            .ToList();
    }

    public void Save()
    {
        // Atomic write: önce temp dosyaya yaz, sonra rename
        var tempPath = _filePath + ".tmp";
        var sb = new StringBuilder();
        sb.AppendLine("URL;Store;HasProducts;ProductCount;LastVisited");
        foreach (var kv in _cache.OrderBy(kv => kv.Key))
        {
            var e = kv.Value;
            sb.AppendLine($"{e.Url};{e.Store};{(e.HasProducts ? "1" : "0")};{e.ProductCount};{e.LastVisited:o}");
        }
        File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);

        // Atomic swap
        if (File.Exists(_filePath))
            File.Replace(tempPath, _filePath, _filePath + ".bak");
        else
            File.Move(tempPath, _filePath);

        _logger.LogInformation("Cache kaydedildi: {Count} sayfa", _cache.Count);
    }
}
