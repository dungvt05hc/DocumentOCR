using System.Globalization;
using System.Text;

namespace DocumentOCR.OcrBenchmark;

/// <summary>Writes <see cref="BenchmarkCsvRow"/> records to a CSV file (RFC 4180 quoting).</summary>
public static class CsvSummaryWriter
{
    private static readonly string[] Header =
    [
        "FileName", "ProviderName", "ModelId", "ProcessingDurationMs", "PageCount",
        "FullTextLength", "AverageConfidence", "SupplierTaxCode", "InvoiceDate",
        "TotalAmount", "WarningCount", "ErrorMessage"
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
                row.ProviderName,
                row.ModelId ?? "",
                row.ProcessingDurationMs.ToString(CultureInfo.InvariantCulture),
                row.PageCount.ToString(CultureInfo.InvariantCulture),
                row.FullTextLength.ToString(CultureInfo.InvariantCulture),
                row.AverageConfidence?.ToString("F4", CultureInfo.InvariantCulture) ?? "",
                row.SupplierTaxCode ?? "",
                row.InvoiceDate ?? "",
                row.TotalAmount ?? "",
                row.WarningCount.ToString(CultureInfo.InvariantCulture),
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
