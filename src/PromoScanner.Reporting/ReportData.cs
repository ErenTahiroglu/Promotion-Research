using PromoScanner.Core;

namespace PromoScanner.Reporting;

/// <summary>
/// Rapor giriş verilerini içeren DTO.
/// </summary>
public sealed class ReportData
{
    public required IReadOnlyCollection<ResultRow> AllResults { get; init; }
    public required IReadOnlyCollection<string> VisitedUrls { get; init; }
    public required IReadOnlyCollection<string> SkippedUrls { get; init; }
    public required IReadOnlyCollection<string> FailedUrls { get; init; }

    public int Phase1Count { get; init; }
    public int Phase2Count { get; init; }
    public int Phase2Updated { get; init; }
}
