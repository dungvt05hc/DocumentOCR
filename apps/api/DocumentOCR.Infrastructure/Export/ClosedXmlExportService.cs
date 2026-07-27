using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
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
    private readonly IReviewTableBuilder _tableBuilder;

    /// <summary>
    /// Reverse alias lookup: canonical legacy FieldName (e.g. "SupplierName") → every review-profile
    /// FieldKey that treats it as an alias (e.g. "MerchantName", "SellerName", "VendorName",
    /// "StoreName"). Built once from <see cref="IDocumentProfileCatalog"/> so the profiles stay the
    /// single source of truth for "these names mean the same thing" — no second hardcoded table.
    /// </summary>
    private readonly Dictionary<string, List<string>> _canonicalToAliasKeys;

    public ClosedXmlExportService(IApplicationDbContext db, IDocumentProfileCatalog profileCatalog, IReviewTableBuilder tableBuilder)
    {
        _db = db;
        _canonicalToAliasKeys = BuildCanonicalToAliasKeys(profileCatalog);
        _tableBuilder = tableBuilder;
    }

    private static Dictionary<string, List<string>> BuildCanonicalToAliasKeys(IDocumentProfileCatalog profileCatalog)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var profiles = Enum.GetValues<DocumentCategory>()
            .Select(profileCatalog.GetProfile)
            .Distinct();

        foreach (var field in profiles.SelectMany(p => p.Sections).SelectMany(s => s.Fields))
        {
            foreach (var canonicalKey in field.AliasFieldNames)
            {
                if (!map.TryGetValue(canonicalKey, out var aliasKeys))
                {
                    aliasKeys = [];
                    map[canonicalKey] = aliasKeys;
                }

                aliasKeys.Add(field.FieldKey);
            }
        }

        return map;
    }

    /// <summary>Looks up <paramref name="canonicalKey"/> directly, falling back to any review-profile field that aliases it (see <see cref="_canonicalToAliasKeys"/>).</summary>
    private string? ResolveFieldValue(IReadOnlyDictionary<string, string?> fields, string canonicalKey)
    {
        if (fields.TryGetValue(canonicalKey, out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct;

        if (!_canonicalToAliasKeys.TryGetValue(canonicalKey, out var aliasKeys))
            return null;

        foreach (var aliasKey in aliasKeys)
        {
            if (fields.TryGetValue(aliasKey, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

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

        var tablesByDocument = documents.ToDictionary(
            d => d.Id,
            d => _tableBuilder.BuildTables(DeserializeTables(d.TablesJson)));

        using var workbook = new XLWorkbook();
        BuildDocumentsSheet(workbook, documents);
        BuildTablesSheet(workbook, documents, tablesByDocument);
        BuildLineItemsSheet(workbook, documents, tablesByDocument);
        BuildWarningsSheet(workbook, documents);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IReadOnlyList<OcrTable> DeserializeTables(string? tablesJson)
    {
        if (string.IsNullOrWhiteSpace(tablesJson)) return [];
        return JsonSerializer.Deserialize<List<OcrTable>>(tablesJson) ?? [];
    }

    private void BuildDocumentsSheet(XLWorkbook workbook, IReadOnlyList<Document> documents)
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
            worksheet.Cell(row, 3).Value = ResolveFieldValue(fields, nameof(FieldName.SupplierName)) ?? string.Empty;
            worksheet.Cell(row, 4).Value = ResolveFieldValue(fields, nameof(FieldName.SupplierTaxCode)) ?? string.Empty;
            worksheet.Cell(row, 5).Value = ResolveFieldValue(fields, nameof(FieldName.InvoiceNumber)) ?? string.Empty;
            SetDateCell(worksheet.Cell(row, 6), ResolveFieldValue(fields, nameof(FieldName.InvoiceDate)));
            SetMoneyCell(worksheet.Cell(row, 7), ResolveFieldValue(fields, nameof(FieldName.SubtotalAmount)));
            SetMoneyCell(worksheet.Cell(row, 8), ResolveFieldValue(fields, nameof(FieldName.VatAmount)));
            SetMoneyCell(worksheet.Cell(row, 9), ResolveFieldValue(fields, nameof(FieldName.TotalAmount)));
            worksheet.Cell(row, 10).Value = ResolveFieldValue(fields, nameof(FieldName.Currency)) ?? string.Empty;
            worksheet.Cell(row, 11).Value = document.ValidationWarnings.Count;
            worksheet.Cell(row, 12).Value = ReviewedStatus(document.Status);
            worksheet.Cell(row, 13).Value = document.CreatedAt;
            worksheet.Cell(row, 13).Style.DateFormat.Format = DateTimeFormat;
        }

        FormatWorksheet(worksheet, DocumentColumns.Length);
    }

    /// <summary>
    /// One row per detected table row (including the header row, so the raw table is fully
    /// recoverable from the sheet). Columns beyond a given table's own width are left blank —
    /// source tables can have different column counts, so the sheet is sized to the widest one
    /// across the exported set rather than assuming a fixed shape. A document with no tables
    /// simply contributes no rows; it never blocks the export.
    /// </summary>
    private void BuildTablesSheet(
        XLWorkbook workbook,
        IReadOnlyList<Document> documents,
        IReadOnlyDictionary<Guid, List<ReviewTable>> tablesByDocument)
    {
        var worksheet = workbook.Worksheets.Add("Tables");

        var maxColumnCount = tablesByDocument.Values
            .SelectMany(tables => tables)
            .Select(t => t.ColumnCount)
            .DefaultIfEmpty(0)
            .Max();

        var columns = new List<ExportColumn> { new("FileName", "Tên tệp"), new("TableId", "Bảng"), new("PageNumber", "Trang"), new("RowIndex", "Dòng") };
        for (var i = 0; i < maxColumnCount; i++)
            columns.Add(new ExportColumn($"Column{i + 1}", $"Cột {i + 1}"));

        WriteHeader(worksheet, columns);

        var row = 2;
        foreach (var document in documents)
        {
            foreach (var table in tablesByDocument.GetValueOrDefault(document.Id, []))
            {
                foreach (var tableRow in table.Rows.OrderBy(r => r.RowIndex))
                {
                    worksheet.Cell(row, 1).Value = document.OriginalFileName;
                    worksheet.Cell(row, 2).Value = table.TableId;
                    SetOptionalNumberCell(worksheet.Cell(row, 3), table.PageNumber);
                    worksheet.Cell(row, 4).Value = tableRow.RowIndex;

                    var cellsByColumn = tableRow.Cells.ToDictionary(c => c.ColumnIndex, c => c.Text);
                    for (var columnIndex = 0; columnIndex < maxColumnCount; columnIndex++)
                    {
                        worksheet.Cell(row, 5 + columnIndex).Value = cellsByColumn.GetValueOrDefault(columnIndex, string.Empty);
                    }

                    row++;
                }
            }
        }

        FormatWorksheet(worksheet, columns.Count);
    }

    private static readonly ExportColumn[] LineItemColumns =
    [
        new("FileName", "Tên tệp"),
        new("LineNumber", "Dòng số"),
        new("Description", "Mô tả"),
        new("Quantity", "Số lượng"),
        new("Unit", "Đơn vị"),
        new("UnitPrice", "Đơn giá"),
        new("Amount", "Thành tiền"),
        new("Currency", "Tiền tệ"),
        new("Confidence", "Độ tin cậy"),
        new("SourceTableId", "Bảng nguồn"),
        new("SourceRowIndex", "Dòng nguồn")
    ];

    /// <summary>One row per candidate line item — only present for documents whose detected tables produced any (see <see cref="IReviewTableBuilder.BuildLineItems"/>).</summary>
    private void BuildLineItemsSheet(
        XLWorkbook workbook,
        IReadOnlyList<Document> documents,
        IReadOnlyDictionary<Guid, List<ReviewTable>> tablesByDocument)
    {
        var worksheet = workbook.Worksheets.Add("LineItems");
        WriteHeader(worksheet, LineItemColumns);

        var row = 2;
        foreach (var document in documents)
        {
            var lineItems = _tableBuilder.BuildLineItems(tablesByDocument.GetValueOrDefault(document.Id, []));
            foreach (var lineItem in lineItems)
            {
                worksheet.Cell(row, 1).Value = document.OriginalFileName;
                worksheet.Cell(row, 2).Value = lineItem.LineNumber;
                worksheet.Cell(row, 3).Value = lineItem.Description ?? string.Empty;
                SetOptionalNumberCell(worksheet.Cell(row, 4), lineItem.Quantity);
                worksheet.Cell(row, 5).Value = lineItem.Unit ?? string.Empty;
                SetOptionalNumberCell(worksheet.Cell(row, 6), lineItem.UnitPrice);
                SetOptionalNumberCell(worksheet.Cell(row, 7), lineItem.Amount);
                worksheet.Cell(row, 8).Value = lineItem.Currency ?? string.Empty;
                SetOptionalNumberCell(worksheet.Cell(row, 9), lineItem.Confidence);
                worksheet.Cell(row, 10).Value = lineItem.SourceTableId;
                worksheet.Cell(row, 11).Value = lineItem.SourceRowIndex;
                row++;
            }
        }

        FormatWorksheet(worksheet, LineItemColumns.Length);
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

    private static void SetOptionalNumberCell(IXLCell cell, int? value)
    {
        if (value.HasValue) cell.Value = value.Value;
        else cell.Value = string.Empty;
    }

    private static void SetOptionalNumberCell(IXLCell cell, decimal? value)
    {
        if (value.HasValue) cell.Value = value.Value;
        else cell.Value = string.Empty;
    }

    private static void SetOptionalNumberCell(IXLCell cell, double? value)
    {
        if (value.HasValue) cell.Value = value.Value;
        else cell.Value = string.Empty;
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
