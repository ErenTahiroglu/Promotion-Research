using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromoScanner.Core;
using PromoScanner.Crawler;
using PromoScanner.Reporting;
using PromoScanner.Scraping;

// Çıktı klasörünü erken oluştur (log dosyası için)
var outputFolder = "output"; // varsayılan, appsettings'ten override edilecek
var outDir = Path.Combine(Directory.GetCurrentDirectory(), outputFolder, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(outDir);
var logPath = Path.Combine(outDir, "run.log");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.SetBasePath(AppContext.BaseDirectory)
           .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
    })
    .ConfigureServices((ctx, services) =>
    {
        // Konfigürasyon
        services.Configure<ScanSettings>(ctx.Configuration.GetSection("Scan"));
        services.Configure<OutputSettings>(ctx.Configuration.GetSection("Output"));

        // Scraping
        services.AddSingleton<IScraperRegistry, ScraperRegistry>();

        // Crawler
        var dataDir = AppContext.BaseDirectory;
        services.AddSingleton<IBlacklistManager>(sp =>
            new BlacklistManager(dataDir, sp.GetRequiredService<ILogger<BlacklistManager>>()));
        services.AddSingleton<IPageCacheManager>(sp =>
            new PageCacheManager(dataDir, sp.GetRequiredService<ILogger<PageCacheManager>>()));
        services.AddSingleton<ICrawlerEngine, CrawlerEngine>();

        // Reporting
        services.AddSingleton<IReportWriter, ReportOrchestrator>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

// OutputSettings'ten gerçek klasör adını al ve gerekirse güncelle
var outputSettings = host.Services.GetRequiredService<IOptions<OutputSettings>>().Value;
if (!string.Equals(outputFolder, outputSettings.Folder, StringComparison.OrdinalIgnoreCase))
{
    outDir = Path.Combine(Directory.GetCurrentDirectory(), outputSettings.Folder, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
    Directory.CreateDirectory(outDir);
    logPath = Path.Combine(outDir, "run.log");
}

// Seed URL'leri oku
var urlsPath = Path.Combine(AppContext.BaseDirectory, "urls.txt");
if (!File.Exists(urlsPath))
{
    Console.WriteLine($"[ERR] urls.txt bulunamadı: {urlsPath}");
    return;
}

var seeds = File.ReadAllLines(urlsPath)
    .Select(l => l.Trim())
    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
    .Distinct()
    .ToList();

// Dosya log yazıcı: console + dosya aynı anda
using var logStream = new StreamWriter(logPath, append: true) { AutoFlush = true };
var originalOut = Console.Out;
Console.SetOut(new TeeTextWriter(originalOut, logStream));

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("PromoScanner başlatıldı");
logger.LogInformation("Çıktı: {Dir}", outDir);
logger.LogInformation("Seed URL sayısı: {Count}", seeds.Count);

// Graceful shutdown
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogWarning("Ctrl+C algılandı — tarama durduruluyor...");
    cts.Cancel();
};

try
{
    // Crawl
    var crawler = host.Services.GetRequiredService<ICrawlerEngine>();
    var crawlResult = await crawler.RunAsync(seeds, cts.Token);

    // Raporla
    var reporter = host.Services.GetRequiredService<IReportWriter>();
    reporter.WriteReports(new ReportData
    {
        AllResults = crawlResult.AllResults,
        VisitedUrls = crawlResult.VisitedUrls,
        SkippedUrls = crawlResult.SkippedUrls,
        FailedUrls = crawlResult.FailedUrls,
        Phase1Count = crawlResult.Phase1Count,
        Phase2Count = crawlResult.Phase2Count,
        Phase2Updated = crawlResult.Phase2Updated,
    }, outDir);

    logger.LogInformation("PromoScanner tamamlandı.");
}
catch (OperationCanceledException)
{
    logger.LogWarning("Tarama kullanıcı tarafından iptal edildi.");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Beklenmeyen hata oluştu");
}
finally
{
    // Console.Out'u geri yükle
    Console.SetOut(originalOut);
}

// Program sınıfı (partial class for top-level statements)
public partial class Program { }

/// <summary>
/// Console çıktısını hem orijinal stream'e hem dosyaya yönlendiren TextWriter.
/// </summary>
internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _primary;
    private readonly TextWriter _secondary;

    public TeeTextWriter(TextWriter primary, TextWriter secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public override System.Text.Encoding Encoding => _primary.Encoding;

    public override void Write(char value) { _primary.Write(value); _secondary.Write(value); }
    public override void Write(string? value) { _primary.Write(value); _secondary.Write(value); }
    public override void WriteLine(string? value) { _primary.WriteLine(value); _secondary.WriteLine(value); }
    public override void Flush() { _primary.Flush(); _secondary.Flush(); }
}