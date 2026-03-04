namespace PromoScanner.Reporting;

/// <summary>
/// Rapor yazıcı arayüzü.
/// </summary>
public interface IReportWriter
{
    /// <summary>
    /// Tüm raporları (Excel, CSV, URL listeleri) belirtilen klasöre yazar.
    /// </summary>
    void WriteReports(ReportData data, string outputDir);
}
