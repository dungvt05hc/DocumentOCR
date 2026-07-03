namespace DocumentOCR.Application.Interfaces;

/// <summary>Generates an Excel workbook for the given document IDs and returns the file bytes.</summary>
public interface IExcelExportService
{
    Task<byte[]> ExportAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default);
}
