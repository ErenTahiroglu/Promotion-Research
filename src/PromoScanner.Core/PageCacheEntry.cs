namespace PromoScanner.Core;

/// <summary>
/// Daha önce ziyaret edilmiş bir sayfanın cache bilgisini tutar.
/// </summary>
public sealed record PageCacheEntry
{
    public string Url { get; init; } = "";
    public string Store { get; init; } = "";
    public bool HasProducts { get; init; }
    public int ProductCount { get; init; }
    public DateTimeOffset LastVisited { get; init; } = DateTimeOffset.Now;
}
