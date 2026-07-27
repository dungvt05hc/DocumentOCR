using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Turns the flat, provider-neutral <see cref="OcrTable"/> cell list into a header+rows shape
/// for review/export, with a best-effort canonical column key (Description/Quantity/UnitPrice/
/// Amount — English and Vietnamese headers), and derives simple candidate line items from the
/// result. This is a basic MVP candidate builder, not full line-item extraction: unparsable cells
/// are kept as raw text rather than failing the row or the table.
/// </summary>
public partial class ReviewTableBuilder : IReviewTableBuilder
{
    private static readonly string[] DescriptionKeywords = ["items", "item", "description", "ten hang", "ten mon", "ten"];
    private static readonly string[] QuantityKeywords = ["quantity", "qty", "sl", "so luong"];
    private static readonly string[] UnitPriceKeywords = ["unit price", "price", "don gia", "gia"];
    private static readonly string[] AmountKeywords = ["amount", "thanh tien", "t tien", "tong tien", "tong"];

    /// <summary>Same "this row is a totals footer, not a line item" signal <c>FieldExtractionService.AddTableFooterCandidates</c> uses, kept in sync deliberately — a row that names a total/subtotal/tax is never a purchasable line.</summary>
    private static readonly string[] FooterRowKeywords =
    [
        "vat", "thue gtgt", "tien thue gtgt", "gtgt",
        "cong tien hang", "subtotal", "sub total", "thanh tien chua thue", "tien hang", "tam tinh",
        "tong thanh toan", "tong cong", "tong tien", "thanh toan", "tong", "total"
    ];

    public List<ReviewTable> BuildTables(IReadOnlyList<OcrTable> tables)
    {
        var result = new List<ReviewTable>();

        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var table = tables[tableIndex];
            if (table.Cells.Count == 0)
            {
                result.Add(new ReviewTable
                {
                    TableId = $"table-{tableIndex}",
                    Title = $"Detected table {tableIndex + 1}",
                    PageNumber = table.PageNumber,
                    RowCount = table.RowCount,
                    ColumnCount = table.ColumnCount,
                    SourceBoundingBoxJson = SerializeBoundingBox(table.BoundingBox)
                });
                continue;
            }

            var rows = table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key).ToList();
            var headerRowIndex = DetermineHeaderRowIndex(rows);

            var columns = BuildColumns(rows.FirstOrDefault(r => r.Key == headerRowIndex), table.ColumnCount);
            var columnKeyByIndex = columns.ToDictionary(c => c.ColumnIndex, c => c.ColumnKey);

            var reviewRows = rows.Select(row => new ReviewTableRow
            {
                RowIndex = row.Key,
                RowType = row.Key == headerRowIndex ? "Header" : "Data",
                Cells = row.Select(cell => new ReviewTableCell
                {
                    RowIndex = cell.RowIndex,
                    ColumnIndex = cell.ColumnIndex,
                    ColumnKey = columnKeyByIndex.GetValueOrDefault(cell.ColumnIndex),
                    Text = cell.Text,
                    IsHeader = row.Key == headerRowIndex,
                    IsEditable = true,
                    SourceBoundingBoxJson = SerializeBoundingBox(cell.BoundingBox)
                }).OrderBy(c => c.ColumnIndex).ToList()
            }).ToList();

            result.Add(new ReviewTable
            {
                TableId = $"table-{tableIndex}",
                Title = $"Detected table {tableIndex + 1}",
                PageNumber = table.PageNumber,
                RowCount = table.RowCount,
                ColumnCount = table.ColumnCount,
                Columns = columns,
                Rows = reviewRows,
                SourceBoundingBoxJson = SerializeBoundingBox(table.BoundingBox)
            });
        }

        return result;
    }

    public List<ReviewLineItem> BuildLineItems(IReadOnlyList<ReviewTable> tables)
    {
        var lineItems = new List<ReviewLineItem>();
        var lineNumber = 1;

        foreach (var table in tables)
        {
            var descriptionColumn = table.Columns.FirstOrDefault(c => c.NormalizedKey == "Description");
            if (descriptionColumn is null) continue;

            var hasAnyAmountColumn = table.Columns.Any(c =>
                c.NormalizedKey is "Quantity" or "UnitPrice" or "Amount");
            if (!hasAnyAmountColumn) continue;

            var quantityColumn = table.Columns.FirstOrDefault(c => c.NormalizedKey == "Quantity");
            var unitPriceColumn = table.Columns.FirstOrDefault(c => c.NormalizedKey == "UnitPrice");
            var amountColumn = table.Columns.FirstOrDefault(c => c.NormalizedKey == "Amount");

            foreach (var row in table.Rows.Where(r => r.RowType != "Header"))
            {
                var descriptionText = CellText(row, descriptionColumn.ColumnIndex)?.Trim();
                if (string.IsNullOrWhiteSpace(descriptionText)) continue;
                if (ContainsAny(NormalizeForSearch(descriptionText), FooterRowKeywords)) continue;

                var warnings = new List<string>();
                var confidence = 0.75;

                var quantity = TryParseNumber(CellText(row, quantityColumn?.ColumnIndex), quantityColumn, warnings, "Quantity", ref confidence);
                var unitPrice = TryParseNumber(CellText(row, unitPriceColumn?.ColumnIndex), unitPriceColumn, warnings, "UnitPrice", ref confidence);
                var amount = TryParseNumber(CellText(row, amountColumn?.ColumnIndex), amountColumn, warnings, "Amount", ref confidence);

                var currencyText = CellText(row, amountColumn?.ColumnIndex) ?? CellText(row, unitPriceColumn?.ColumnIndex);

                lineItems.Add(new ReviewLineItem
                {
                    LineNumber = lineNumber++,
                    Description = descriptionText,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Amount = amount,
                    Currency = DetectCurrency(currencyText),
                    Confidence = confidence,
                    SourceTableId = table.TableId,
                    SourceRowIndex = row.RowIndex,
                    IsEditable = true,
                    Warnings = warnings
                });
            }
        }

        return lineItems;
    }

    private static decimal? TryParseNumber(
        string? text,
        ReviewTableColumn? column,
        List<string> warnings,
        string label,
        ref double confidence)
    {
        if (column is null) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (TryParseTolerantDecimal(text, out var value)) return value;

        warnings.Add($"{label} could not be parsed from \"{text}\".");
        confidence = Math.Min(confidence, 0.4);
        return null;
    }

    private static string? CellText(ReviewTableRow row, int? columnIndex)
    {
        if (columnIndex is null) return null;
        return row.Cells.FirstOrDefault(c => c.ColumnIndex == columnIndex)?.Text;
    }

    private static int DetermineHeaderRowIndex(List<IGrouping<int, OcrTableCell>> rows)
    {
        var headerRow = rows.FirstOrDefault(row =>
            row.Count(c => string.Equals(c.Kind, "columnHeader", StringComparison.OrdinalIgnoreCase)) > row.Count() / 2.0);

        return headerRow?.Key ?? rows[0].Key;
    }

    private static List<ReviewTableColumn> BuildColumns(IGrouping<int, OcrTableCell>? headerRow, int columnCount)
    {
        var headerCellsByIndex = headerRow?.ToDictionary(c => c.ColumnIndex, c => c.Text) ?? [];
        var columns = new List<ReviewTableColumn>();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        var width = Math.Max(columnCount, headerCellsByIndex.Count == 0 ? 0 : headerCellsByIndex.Keys.Max() + 1);
        for (var columnIndex = 0; columnIndex < width; columnIndex++)
        {
            var label = headerCellsByIndex.GetValueOrDefault(columnIndex, string.Empty);
            var normalizedKey = MatchColumnKeyword(label);

            var columnKey = normalizedKey ?? $"col{columnIndex}";
            if (!usedKeys.Add(columnKey))
            {
                columnKey = $"{columnKey}_{columnIndex}";
            }

            columns.Add(new ReviewTableColumn
            {
                ColumnIndex = columnIndex,
                ColumnKey = columnKey,
                Label = label,
                NormalizedKey = normalizedKey
            });
        }

        return columns;
    }

    private static string? MatchColumnKeyword(string label)
    {
        var search = NormalizeForSearch(label);
        if (search.Length == 0) return null;

        if (ContainsAny(search, DescriptionKeywords)) return "Description";
        if (ContainsAny(search, QuantityKeywords)) return "Quantity";
        if (ContainsAny(search, UnitPriceKeywords)) return "UnitPrice";
        if (ContainsAny(search, AmountKeywords)) return "Amount";
        return null;
    }

    private static bool ContainsAny(string searchText, IEnumerable<string> keywords) =>
        keywords.Any(keyword => searchText.Contains(keyword, StringComparison.Ordinal));

    private static string? SerializeBoundingBox(BoundingBox? box) =>
        box is null ? null : JsonSerializer.Serialize(box);

    private static string? DetectCurrency(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (CurrencyCodePattern().Match(text) is { Success: true } codeMatch) return codeMatch.Value.ToUpperInvariant();
        if (text.Contains('₫') || text.Contains('đ', StringComparison.OrdinalIgnoreCase)) return "VND";
        if (text.Contains('$')) return "USD";
        if (text.Contains('€')) return "EUR";
        if (text.Contains('£')) return "GBP";
        return null;
    }

    private static bool TryParseTolerantDecimal(string text, out decimal value)
    {
        var match = NumberPattern().Match(text);
        if (!match.Success)
        {
            value = default;
            return false;
        }

        var cleaned = match.Value;

        if (EuropeanMoneyPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(cleaned.Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        if (UsMoneyPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(cleaned.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        // A single dot followed by 1–2 digits (e.g. "10.00", "725.30") is a plain decimal, not a
        // thousands-grouped integer — stripping the dot here would silently turn "10.00" into 1000.
        if (SimpleDecimalPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        var plain = cleaned.Replace(".", "").Replace(",", "");
        return decimal.TryParse(plain, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeForSearch(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c switch
                {
                    'Đ' => 'd',
                    'đ' => 'd',
                    '_' => ' ',
                    _ => char.ToLowerInvariant(c)
                });
            }
        }

        return WhitespacePattern().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    [GeneratedRegex(@"-?\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex EuropeanMoneyPattern();

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex UsMoneyPattern();

    [GeneratedRegex(@"^-?\d+\.\d{1,2}$")]
    private static partial Regex SimpleDecimalPattern();

    [GeneratedRegex(@"\b(VND|VNĐ|USD|EUR|GBP|JPY|CNY)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyCodePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
