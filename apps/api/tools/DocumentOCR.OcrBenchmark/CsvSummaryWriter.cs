using System.Globalization;
using System.Text;

namespace DocumentOCR.OcrBenchmark;

/// <summary>Writes <see cref="BenchmarkCsvRow"/> records to a CSV file (RFC 4180 quoting).</summary>
public static class CsvSummaryWriter
{
    private static readonly string[] Header =
    [
        "FileName", "DocumentCategory", "ProviderName", "ModelId", "Features",
        "ProcessingDurationMs", "PageCount", "FullTextLength", "LineCount", "WordCount",
        "ParagraphCount", "TableCount", "KeyValuePairCount", "AverageConfidence",
        "ExtractedSupplierName", "ExpectedSupplierName", "SupplierNameMatched",
        "ExtractedSupplierTaxCode", "ExpectedSupplierTaxCode", "TaxCodeMatched",
        "ExtractedInvoiceNumber", "ExpectedInvoiceNumber", "InvoiceNumberMatched",
        "ExtractedInvoiceDate", "ExpectedInvoiceDate", "InvoiceDateMatched",
        "ExtractedSubtotalAmount", "ExpectedSubtotalAmount", "SubtotalMatched",
        "ExtractedVatAmount", "ExpectedVatAmount", "VatMatched",
        "ExtractedTotalAmount", "ExpectedTotalAmount", "TotalMatched",
        "ExtractedCurrency", "ExpectedCurrency", "CurrencyMatched",
        "FieldAccuracyPercent",
        "WarningCount", "RawProviderResponsePath", "NormalizedOcrResultPath", "ErrorMessage"
    ];

    public static async Task WriteAsync(string filePath, IReadOnlyList<BenchmarkCsvRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));

        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.FileName,
                row.DocumentCategory ?? "",
                row.ProviderName,
                row.ModelId ?? "",
                row.Features,
                row.ProcessingDurationMs.ToString(CultureInfo.InvariantCulture),
                row.PageCount.ToString(CultureInfo.InvariantCulture),
                row.FullTextLength.ToString(CultureInfo.InvariantCulture),
                row.LineCount.ToString(CultureInfo.InvariantCulture),
                row.WordCount.ToString(CultureInfo.InvariantCulture),
                row.ParagraphCount.ToString(CultureInfo.InvariantCulture),
                row.TableCount.ToString(CultureInfo.InvariantCulture),
                row.KeyValuePairCount.ToString(CultureInfo.InvariantCulture),
                row.AverageConfidence?.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                row.ExtractedSupplierName ?? "",
                row.ExpectedSupplierName ?? "",
                FormatMatch(row.SupplierNameMatched),
                row.ExtractedSupplierTaxCode ?? "",
                row.ExpectedSupplierTaxCode ?? "",
                FormatMatch(row.TaxCodeMatched),
                row.ExtractedInvoiceNumber ?? "",
                row.ExpectedInvoiceNumber ?? "",
                FormatMatch(row.InvoiceNumberMatched),
                row.ExtractedInvoiceDate ?? "",
                row.ExpectedInvoiceDate ?? "",
                FormatMatch(row.InvoiceDateMatched),
                row.ExtractedSubtotalAmount ?? "",
                row.ExpectedSubtotalAmount ?? "",
                FormatMatch(row.SubtotalMatched),
                row.ExtractedVatAmount ?? "",
                row.ExpectedVatAmount ?? "",
                FormatMatch(row.VatMatched),
                row.ExtractedTotalAmount ?? "",
                row.ExpectedTotalAmount ?? "",
                FormatMatch(row.TotalMatched),
                row.ExtractedCurrency ?? "",
                row.ExpectedCurrency ?? "",
                FormatMatch(row.CurrencyMatched),
                row.FieldAccuracyPercent?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                row.WarningCount.ToString(CultureInfo.InvariantCulture),
                row.RawProviderResponsePath ?? "",
                row.NormalizedOcrResultPath ?? "",
                row.ErrorMessage ?? ""
            };

            sb.AppendLine(string.Join(',', fields.Select(Escape)));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
    }

    // Blank means "not evaluated" (no ground truth value for that field) rather than a match/mismatch.
    private static string FormatMatch(bool? matched) => matched switch
    {
        true => "True",
        false => "False",
        null => ""
    };

    private static string Escape(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
            return field;

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
