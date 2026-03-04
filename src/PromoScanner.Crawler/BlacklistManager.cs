using System.Text;
using Microsoft.Extensions.Logging;

namespace PromoScanner.Crawler;

/// <summary>
/// quote_blacklist.txt dosyasını yöneten sınıf.
/// Sadece gerçekten ziyaret edilip fiyat bulunamayan URL'ler eklenir.
/// </summary>
public sealed class BlacklistManager : IBlacklistManager
{
    private readonly string _filePath;
    private readonly HashSet<string> _initial;
    private readonly HashSet<string> _confirmed;
    private readonly ILogger<BlacklistManager> _logger;

    // Lazy cached merged set
    private HashSet<string>? _mergedCache;

    public BlacklistManager(string dataDir, ILogger<BlacklistManager> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(dataDir, "quote_blacklist.txt");
        _initial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(_filePath))
        {
            foreach (var line in File.ReadAllLines(_filePath)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')))
            {
                _initial.Add(line);
            }
            _logger.LogInformation("Blacklist yüklendi: {Count} URL atlanacak", _initial.Count);
        }
    }

    public bool IsBlacklisted(string url) => _initial.Contains(url) || _confirmed.Contains(url);

    public void AddConfirmed(string url)
    {
        if (_confirmed.Add(url))
            _mergedCache = null; // Invalidate cache
    }

    public int Count => AllUrls.Count;
    public int NewCount => _confirmed.Count(u => !_initial.Contains(u));

    public IReadOnlySet<string> AllUrls
    {
        get
        {
            if (_mergedCache != null) return _mergedCache;

            var all = new HashSet<string>(_initial, StringComparer.OrdinalIgnoreCase);
            foreach (var u in _confirmed) all.Add(u);
            _mergedCache = all;
            return all;
        }
    }

    public void Save()
    {
        var all = AllUrls;
        if (all.Count == 0) return;

        List<string> content =
        [
            $"# PromoScanner Quote Blacklist - {DateTimeOffset.Now:dd.MM.yyyy HH:mm}",
            $"# {all.Count} URL (sadece ziyaret edilip fiyatsız bulunan sayfalar)",
            .. all.OrderBy(x => x)
        ];
        File.WriteAllLines(_filePath, content, Encoding.UTF8);
        _logger.LogInformation("Blacklist güncellendi: {Total} toplam ({New} yeni doğrulanmış)", all.Count, NewCount);
    }
}
