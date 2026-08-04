using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Orchestrates an accounting-service export (Thông tư 88/2021/TT-BTC report formats): loads the
/// client and its in-range documents, runs <see cref="IAccountingReportBuilder"/>, and renders the
/// result via <see cref="IAccountingReportWorkbookRenderer"/>. Separate from
/// <see cref="IExcelExportService"/>/<c>ExportService</c>, which remains the unmodified per-document
/// data-dump export.
/// </summary>
public interface IAccountingReportExportService
{
    Task<(byte[] Bytes, string FileName)> ExportAsync(
        AccountingReportType type,
        Guid clientProfileId,
        DateOnly from,
        DateOnly to,
        Guid organizationId,
        CancellationToken ct = default);
}
