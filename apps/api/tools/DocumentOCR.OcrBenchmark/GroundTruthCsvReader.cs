using System.Text;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Reads <c>data/vietnam-samples/ground-truth.csv</c> into <see cref="GroundTruthRow"/>s keyed by
/// file name, so <see cref="BenchmarkFileProcessor"/> can look up the expected values for each
/// sample file it OCRs.
/// </summary>
public static class GroundTruthCsvReader
{
    private static readonly string[] ExpectedHeader =
    [
        "FileName", "DocumentCategory", "DocumentSubType", "ExpectedSupplierName",
        "ExpectedSupplierTaxCode", "ExpectedBuyerName", "ExpectedBuyerTaxCode",
        "ExpectedInvoiceNumber", "ExpectedInvoiceDate", "ExpectedSubtotalAmount",
        "ExpectedVatAmount", "ExpectedTotalAmount", "ExpectedCurrency", "QualityLevel", "Notes"
    ];

    /// <summary>
    /// Loads and parses the ground-truth CSV at <paramref name="path"/>. Returns an empty,
    /// case-insensitive-by-FileName dictionary if the file doesn't exist — ground truth is
    /// optional, and the benchmark tool must still run (without comparison columns) without it.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, GroundTruthRow>> LoadAsync(
        string path, CancellationToken ct)
    {
        var rows = new Dictionary<string, GroundTruthRow>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return rows;

        var lines = await File.ReadAllLinesAsync(path, ct);
        if (lines.Length == 0) return rows;

        var header = ParseLine(lines[0]);
        var columnIndex = ExpectedHeader
            .Select(name => Array.FindIndex(header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (columnIndex[0] < 0)
        {
            throw new InvalidDataException(
                $"'{path}' is missing a required 'FileName' column.");
        }

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex])) continue;

            var fields = ParseLine(lines[lineIndex]);

            string? Get(int headerPosition)
            {
                var index = columnIndex[headerPosition];
                if (index < 0 || index >= fields.Length) return null;
                var value = fields[index].Trim();
                return value.Length == 0 ? null : value;
            }

            var fileName = Get(0);
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            rows[fileName] = new GroundTruthRow(
                FileName: fileName,
                DocumentCategory: Get(1),
                DocumentSubType: Get(2),
                ExpectedSupplierName: Get(3),
                ExpectedSupplierTaxCode: Get(4),
                ExpectedBuyerName: Get(5),
                ExpectedBuyerTaxCode: Get(6),
                ExpectedInvoiceNumber: Get(7),
                ExpectedInvoiceDate: Get(8),
                ExpectedSubtotalAmount: Get(9),
                ExpectedVatAmount: Get(10),
                ExpectedTotalAmount: Get(11),
                ExpectedCurrency: Get(12),
                QualityLevel: Get(13),
                Notes: Get(14));
        }

        return rows;
    }

    /// <summary>Parses one RFC 4180 CSV line, honoring quoted fields (with embedded commas/escaped quotes).</summary>
    internal static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
