using DocumentOCR.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

public class ExportService
{
    private readonly IApplicationDbContext _db;
    private readonly IExcelExportService _excel;

    public ExportService(IApplicationDbContext db, IExcelExportService excel)
    {
        _db = db;
        _excel = excel;
    }

    public async Task<(byte[] Bytes, string FileName)> ExportToExcelAsync(
        IEnumerable<Guid> documentIds,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var ids = documentIds.ToList();

        var existing = await _db.Documents
            .Where(d => ids.Contains(d.Id) && d.OrganizationId == organizationId)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var missing = ids.Except(existing).ToList();
        if (missing.Count > 0)
            throw new KeyNotFoundException($"Documents not found: {string.Join(", ", missing)}");

        var bytes = await _excel.ExportAsync(ids, ct);
        var fileName = $"DocumentOCR_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return (bytes, fileName);
    }
}
