using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Infrastructure.Export;

public partial class ClosedXmlExportService : IExcelExportService
{
    private const string DateFormat = "dd/MM/yyyy";
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";
    private const string MoneyFormat = "#,##0";

    private static readonly ExportColumn[] DocumentColumns =
    [
        new("FileName", "Tên tệp"),
        new("DocumentType", "Loại chứng từ"),
        new(nameof(FieldName.SupplierName), "Tên nhà cung cấp"),
        new(nameof(FieldName.SupplierTaxCode), "Mã số thuế"),
        new(nameof(FieldName.InvoiceNumber), "Số hóa đơn"),
        new(nameof(FieldName.InvoiceDate), "Ngày hóa đơn"),
        new(nameof(FieldName.SubtotalAmount), "Cộng tiền hàng"),
        new(nameof(FieldName.VatAmount), "Thuế GTGT"),
        new(nameof(FieldName.TotalAmount), "Tổng thanh toán"),
        new(nameof(FieldName.Currency), "Tiền tệ"),
        new("WarningCount", "Số cảnh báo"),
        new("ReviewedStatus", "Trạng thái duyệt"),
        new("CreatedAt", "Ngày tải lên")
    ];

    private static readonly ExportColumn[] WarningColumns =
    [
        new("FileName", "Tên tệp"),
        new("FieldName", "Tên trường"),
        new("WarningCode", "Mã cảnh báo"),
        new("Message", "Nội dung"),
        new("Severity", "Mức độ")
    ];

    private readonly IApplicationDbContext _db;

    public ClosedXmlExportService(IApplicationDbContext db) => _db = db;

    public async Task<byte[]> ExportAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.ToList();
        var order = ids.Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index);

        var documents = await _db.Documents
            .Where(d => ids.Contains(d.Id))
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .ToListAsync(ct);

        documents = documents
            .OrderBy(d => order.GetValueOrDefault(d.Id, int.MaxValue))
            .ThenBy(d => d.CreatedAt)
            .ToList();

        using var workbook = new XLWorkbook();
        BuildDocumentsSheet(workbook, documents);
        BuildWarningsSheet(workbook, documents);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildDocumentsSheet(XLWorkbook workbook, IReadOnlyList<Document> documents)
    {
        var worksheet = workbook.Worksheets.Add("Documents");
        WriteHeader(worksheet, DocumentColumns);

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            var row = index + 2;
            var fields = document.Fields.ToDictionary(f => f.FieldName, f => FieldValue(f), StringComparer.Ordinal);

            worksheet.Cell(row, 1).Value = document.OriginalFileName;
            worksheet.Cell(row, 2).Value = document.DocumentType.ToString();
            worksheet.Cell(row, 3).Value = fields.GetValueOrDefault(nameof(FieldName.SupplierName)) ?? string.Empty;
            worksheet.Cell(row, 4).Value = fields.GetValueOrDefault(nameof(FieldName.SupplierTaxCode)) ?? string.Empty;
            worksheet.Cell(row, 5).Value = fields.GetValueOrDefault(nameof(FieldName.InvoiceNumber)) ?? string.Empty;
            SetDateCell(worksheet.Cell(row, 6), fields.GetValueOrDefault(nameof(FieldName.InvoiceDate)));
            SetMoneyCell(worksheet.Cell(row, 7), fields.GetValueOrDefault(nameof(FieldName.SubtotalAmount)));
            SetMoneyCell(worksheet.Cell(row, 8), fields.GetValueOrDefault(nameof(FieldName.VatAmount)));
            SetMoneyCell(worksheet.Cell(row, 9), fields.GetValueOrDefault(nameof(FieldName.TotalAmount)));
            worksheet.Cell(row, 10).Value = fields.GetValueOrDefault(nameof(FieldName.Currency)) ?? string.Empty;
            worksheet.Cell(row, 11).Value = document.ValidationWarnings.Count;
            worksheet.Cell(row, 12).Value = ReviewedStatus(document.Status);
            worksheet.Cell(row, 13).Value = document.CreatedAt;
            worksheet.Cell(row, 13).Style.DateFormat.Format = DateTimeFormat;
        }

        FormatWorksheet(worksheet, DocumentColumns.Length);
    }

    private static void BuildWarningsSheet(XLWorkbook workbook, IReadOnlyList<Document> documents)
    {
        var worksheet = workbook.Worksheets.Add("Warnings");
        WriteHeader(worksheet, WarningColumns);

        var row = 2;
        foreach (var document in documents)
        {
            foreach (var warning in document.ValidationWarnings.OrderBy(w => w.FieldName).ThenBy(w => w.WarningCode))
            {
                worksheet.Cell(row, 1).Value = document.OriginalFileName;
                worksheet.Cell(row, 2).Value = warning.FieldName ?? string.Empty;
                worksheet.Cell(row, 3).Value = warning.WarningCode ?? string.Empty;
                worksheet.Cell(row, 4).Value = warning.Message;
                worksheet.Cell(row, 5).Value = warning.Severity.ToString();
                row++;
            }
        }

        FormatWorksheet(worksheet, WarningColumns.Length);
    }

    private static void WriteHeader(IXLWorksheet worksheet, IReadOnlyList<ExportColumn> columns)
    {
        for (var column = 0; column < columns.Count; column++)
        {
            worksheet.Cell(1, column + 1).Value = columns[column].Header;
        }

        var header = worksheet.Range(1, 1, 1, columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F5F8B");
        header.Style.Font.FontColor = XLColor.White;
    }

    private static void FormatWorksheet(IXLWorksheet worksheet, int columnCount)
    {
        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, Math.Max(1, worksheet.LastRowUsed()?.RowNumber() ?? 1), columnCount).SetAutoFilter();
        worksheet.Columns(1, columnCount).AdjustToContents();
    }

    private static void SetDateCell(IXLCell cell, string? value)
    {
        if (TryParseDate(value, out var date))
        {
            cell.Value = date.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = DateFormat;
            return;
        }

        cell.Value = value ?? string.Empty;
    }

    private static void SetMoneyCell(IXLCell cell, string? value)
    {
        if (TryParseMoney(value, out var amount))
        {
            cell.Value = amount;
            cell.Style.NumberFormat.Format = MoneyFormat;
            return;
        }

        cell.Value = value ?? string.Empty;
    }

    private static string ReviewedStatus(DocumentStatus status)
    {
        return status switch
        {
            DocumentStatus.Reviewed or DocumentStatus.Exported => "Đã duyệt",
            _ => "Chưa duyệt"
        };
    }

    private static string? FieldValue(ExtractedField field)
    {
        return !string.IsNullOrWhiteSpace(field.NormalizedValue)
            ? field.NormalizedValue
            : field.RawValue;
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var match = DatePattern().Match(value);
        var candidate = match.Success ? match.Value : value.Trim();

        string[] formats =
        [
            "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy", "d-M-yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "dd/MM/yy", "d/M/yy"
        ];

        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }

        return false;
    }

    private static bool TryParseMoney(string? rawValue, out decimal value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(rawValue)) return false;

        var match = MoneyPattern().Matches(rawValue)
            .Cast<Match>()
            .LastOrDefault(m => m.Success && m.Value.Any(char.IsDigit));

        if (match is null) return false;

        var cleaned = MoneyCleanupPattern().Replace(match.Value, " ").Trim();
        cleaned = WhitespacePattern().Replace(cleaned, " ");

        if (EuropeanMoneyPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(
                cleaned.Replace(".", "").Replace(",", "."),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out value);
        }

        if (UsMoneyPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(
                cleaned.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out value);
        }

        var plain = cleaned.Replace(".", "").Replace(",", "").Replace(" ", "");
        return decimal.TryParse(plain, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private sealed record ExportColumn(string Key, string Header);

    [GeneratedRegex(@"\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}[-/]\d{1,2}[-/]\d{1,2}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đ)?\s*-?\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"[^\d.,\s-]", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyCleanupPattern();

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex EuropeanMoneyPattern();

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex UsMoneyPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
