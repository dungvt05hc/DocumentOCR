namespace DocumentOCR.OcrBenchmark;

/// <summary>Maps sample-file extensions to the MIME content types the MVP supports (PDF, JPG, PNG).</summary>
public static class ContentTypeMapper
{
    private static readonly Dictionary<string, string> ContentTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    /// <summary>Returns the content type for a supported extension, or null if the extension is not supported.</summary>
    public static string? TryGetContentType(string filePath) =>
        ContentTypeByExtension.TryGetValue(Path.GetExtension(filePath), out var contentType)
            ? contentType
            : null;
}
