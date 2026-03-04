using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace PromoScanner.Reporting;

/// <summary>
/// CSV rapor yazıcısı. Türkçe ayraç (;) kullanır.
/// </summary>
public static class CsvReportWriter
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.GetCultureInfo("tr-TR"))
    {
        Delimiter = ";",
        HasHeaderRecord = true
    };

    public static void WriteCsv<T>(string path, IEnumerable<T> rows)
    {
        using var sw = new StreamWriter(path, false, new UTF8Encoding(true));
        using var csv = new CsvWriter(sw, CsvConfig);
        csv.WriteHeader<T>();
        csv.NextRecord();
        foreach (var r in rows) { csv.WriteRecord(r); csv.NextRecord(); }
    }
}
