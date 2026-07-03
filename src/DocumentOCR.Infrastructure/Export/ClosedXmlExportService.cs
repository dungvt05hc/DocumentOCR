using ClosedXML.Excel;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Infrastructure.Export;

public class ClosedXmlExportService : IExcelExportService
{
    private static readonly string[] ColumnOrder =
    [
        nameof(FieldName.SupplierName),
        nameof(FieldName.SupplierTaxCode),
        nameof(FieldName.InvoiceNumber),
        nameof(FieldName.InvoiceDate),
        nameof(FieldName.SubtotalAmount),
        nameof(FieldName.VatAmount),
        nameof(FieldName.TotalAmount),
        nameof(FieldName.Currency),
        nameof(FieldName.DocumentType),
        nameof(FieldName.Notes)
    ];

    private static readonly HashSet<string> AmountColumns = new(StringComparer.Ordinal)
    {
        nameof(FieldName.SubtotalAmount),
        nameof(FieldName.VatAmount),
        nameof(FieldName.TotalAmount)
    };

    private readonly IApplicationDbContext _db;

    public ClosedXmlExportService(IApplicationDbContext db) => _db = db;

    public async Task<byte[]> ExportAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.ToList();

        var documents = await _db.Documents
            .Where(d => ids.Contains(d.Id))
            .Include(d => d.Fields)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Documents");

        // ── Header row ───────────────────────────────────────────────────────────
        ws.Cell(1, 1).Value = "File Name";
        ws.Cell(1, 2).Value = "Status";
        ws.Cell(1, 3).Value = "Document Type";

        for (var i = 0; i < ColumnOrder.Length; i++)
            ws.Cell(1, i + 4).Value = ColumnOrder[i].ToString();

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#2D6A9F");
        headerRow.Style.Font.FontColor = XLColor.White;

        // ── Data rows ────────────────────────────────────────────────────────────
        for (var row = 0; row < documents.Count; row++)
        {
            var doc = documents[row];
            var excelRow = row + 2;

            ws.Cell(excelRow, 1).Value = doc.OriginalFileName;
            ws.Cell(excelRow, 2).Value = doc.Status.ToString();
            ws.Cell(excelRow, 3).Value = doc.DocumentType.ToString();

            var fieldLookup = doc.Fields.ToDictionary(f => f.FieldName, f => f.NormalizedValue);

            for (var col = 0; col < ColumnOrder.Length; col++)
            {
                var fieldName = ColumnOrder[col];
                var value = fieldLookup.GetValueOrDefault(fieldName);
                var cell = ws.Cell(excelRow, col + 4);

                // Apply numeric type to amount columns so Excel can sum them
                if (AmountColumns.Contains(fieldName))
                {
                    if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var amount))
                    {
                        cell.Value = amount;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                    }
                    else
                    {
                        cell.Value = value ?? string.Empty;
                    }
                }
                else
                {
                    cell.Value = value ?? string.Empty;
                }
            }
        }

        // ── Auto-fit + freeze header ─────────────────────────────────────────────
        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
