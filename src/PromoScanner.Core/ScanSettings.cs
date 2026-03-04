namespace PromoScanner.Core;

/// <summary>
/// appsettings.json → "Scan" bölümüne bağlanan konfigürasyon sınıfı.
/// </summary>
public sealed class ScanSettings
{
    public bool Headless { get; set; } = true;
    public int MaxConcurrency { get; set; } = 3;
    public int NavigationTimeoutMs { get; set; } = 45_000;
    public bool RespectRobotsTxt { get; set; } = true;
    public bool CheckTermsPage { get; set; } = true;
    public string UserAgent { get; set; } = "PromoScanner/1.0";

    // Crawling limitleri
    public int MaxNewPages { get; set; } = 1500;
    public int MaxNewPerSite { get; set; } = 300;
    public int MaxRefreshPages { get; set; } = 800;
    public int MaxRefreshPerSite { get; set; } = 200;

    // Zamanaşımı (ms)
    public int PageLoadWaitMs { get; set; } = 500;
    public int BrowserTimeoutMs { get; set; } = 90_000;
    public int ElementTimeoutMs { get; set; } = 10_000;

    // KDV oranı (fiyat normalizasyonu için, 0.20 = %20)
    public decimal KdvRate { get; set; } = 0.20m;
}
