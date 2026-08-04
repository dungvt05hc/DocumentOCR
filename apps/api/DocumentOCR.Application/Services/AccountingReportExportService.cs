using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

/// <summary>
/// Orchestrator for the Thông tư 88/2021/TT-BTC accounting report exports: loads the client and its
/// in-range documents, delegates aggregation to <see cref="IAccountingReportBuilder"/> and rendering
/// to <see cref="IAccountingReportWorkbookRenderer"/>. Query/filename concerns live here; grouping/
/// totals math never does (see <see cref="IAccountingReportBuilder"/>), and Excel drawing never does
/// (see <see cref="IAccountingReportWorkbookRenderer"/>).
/// </summary>
public partial class AccountingReportExportService : IAccountingReportExportService
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingReportBuilder _builder;
    private readonly IAccountingReportWorkbookRenderer _renderer;

    public AccountingReportExportService(
        IApplicationDbContext db,
        IAccountingReportBuilder builder,
        IAccountingReportWorkbookRenderer renderer)
    {
        _db = db;
        _builder = builder;
        _renderer = renderer;
    }

    public async Task<(byte[] Bytes, string FileName)> ExportAsync(
        AccountingReportType type,
        Guid clientProfileId,
        DateOnly from,
        DateOnly to,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var client = await _db.ClientProfiles
            .FirstOrDefaultAsync(c => c.Id == clientProfileId && c.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Client {clientProfileId} not found.");

        var documents = await _db.Documents
            .Where(d => d.OrganizationId == organizationId
                        && d.ClientProfileId == clientProfileId
                        && (d.Status == DocumentStatus.Processed || d.Status == DocumentStatus.Reviewed))
            .Include(d => d.Fields)
            .ToListAsync(ct);

        var header = new Application.Models.AccountingReportHeader(client.Name, client.Address, client.TaxCode, client.ClientType);
        var model = _builder.Build(type, header, from, to, documents);
        var bytes = _renderer.Render(model);

        var fileName = $"{SlugifyForFileName(client.Name)}_{type}_{from:yyyyMM}-{to:yyyyMM}.xlsx";

        return (bytes, fileName);
    }

    /// <summary>Strips Vietnamese diacritics and any character unsafe in a downloaded file name — the client's own free-text <c>Name</c> must never end up unsanitized in a <c>Content-Disposition</c> file name.</summary>
    private static string SlugifyForFileName(string name)
    {
        var normalized = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c == 'đ' || c == 'Đ' ? 'd' : c);
        }

        var withoutDiacritics = builder.ToString().Normalize(NormalizationForm.FormC);
        var safe = UnsafeFileNameCharsPattern().Replace(withoutDiacritics, "_");
        return CollapseUnderscoresPattern().Replace(safe, "_").Trim('_');
    }

    [GeneratedRegex(@"[^A-Za-z0-9_-]+")]
    private static partial Regex UnsafeFileNameCharsPattern();

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex CollapseUnderscoresPattern();
}
