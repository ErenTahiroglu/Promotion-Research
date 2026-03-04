namespace PromoScanner.Crawler;

/// <summary>
/// Öncelikli URL kuyruğu. Düşük rakam = yüksek öncelik.
/// O(1) Count property ile performanslı.
/// </summary>
public sealed class CrawlQueue
{
    private readonly SortedList<int, Queue<(string Seed, string Url)>> _queues = new();
    private int _totalCount;

    public void Enqueue(string seed, string url, int priority = 1)
    {
        if (!_queues.ContainsKey(priority))
            _queues[priority] = new Queue<(string, string)>();
        _queues[priority].Enqueue((seed, url));
        _totalCount++;
    }

    public (string Seed, string Url)? Dequeue()
    {
        foreach (var kv in _queues)
        {
            if (kv.Value.Count > 0)
            {
                _totalCount--;
                return kv.Value.Dequeue();
            }
        }
        return null;
    }

    public int Count => _totalCount;
}
