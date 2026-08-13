namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Reads <c>pairs.csv</c> (columns: <c>XmlFile,PdfFile</c>) — same-invoice XML/PDF pairs used by
/// the XML-as-ground-truth comparison pass (see Program.cs). Optional: an empty/missing file just
/// means that comparison pass doesn't run, same convention as <see cref="GroundTruthCsvReader"/>.
/// </summary>
public static class PdfXmlPairCsvReader
{
    public static async Task<IReadOnlyList<PdfXmlPair>> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];

        var lines = await File.ReadAllLinesAsync(path, ct);
        if (lines.Length == 0) return [];

        var header = GroundTruthCsvReader.ParseLine(lines[0]);
        var xmlIndex = Array.FindIndex(header, h => string.Equals(h, "XmlFile", StringComparison.OrdinalIgnoreCase));
        var pdfIndex = Array.FindIndex(header, h => string.Equals(h, "PdfFile", StringComparison.OrdinalIgnoreCase));

        if (xmlIndex < 0 || pdfIndex < 0)
            throw new InvalidDataException($"'{path}' must have 'XmlFile' and 'PdfFile' columns.");

        var pairs = new List<PdfXmlPair>();

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex])) continue;

            var fields = GroundTruthCsvReader.ParseLine(lines[lineIndex]);
            if (xmlIndex >= fields.Length || pdfIndex >= fields.Length) continue;

            var xmlFile = fields[xmlIndex].Trim();
            var pdfFile = fields[pdfIndex].Trim();
            if (xmlFile.Length == 0 || pdfFile.Length == 0) continue;

            pairs.Add(new PdfXmlPair(xmlFile, pdfFile));
        }

        return pairs;
    }
}
