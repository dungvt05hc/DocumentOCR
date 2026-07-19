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
        "ExtractedSupplierName", "ExtractedSupplierTaxCode", "ExtractedInvoiceNumber",
        "ExtractedInvoiceDate", "ExtractedSubtotalAmount", "ExtractedVatAmount",
        "ExtractedTotalAmount", "ExtractedCurrency", "WarningCount",
        "RawProviderResponsePath", "NormalizedOcrResultPath", "ErrorMessage"
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
                row.ExtractedSupplierTaxCode ?? "",
                row.ExtractedInvoiceNumber ?? "",
                row.ExtractedInvoiceDate ?? "",
                row.ExtractedSubtotalAmount ?? "",
                row.ExtractedVatAmount ?? "",
                row.ExtractedTotalAmount ?? "",
                row.ExtractedCurrency ?? "",
                row.WarningCount.ToString(CultureInfo.InvariantCulture),
                row.RawProviderResponsePath ?? "",
                row.NormalizedOcrResultPath ?? "",
                row.ErrorMessage ?? ""
            };

            sb.AppendLine(string.Join(',', fields.Select(Escape)));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
    }

    private static string Escape(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
            return field;

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
