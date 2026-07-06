namespace DocumentOCR.Application.Models;

/// <summary>OCR results for a single page of the document.</summary>
public sealed class OcrPageResult
{
    /// <summary>1-based page number.</summary>
    public int PageNumber { get; init; }

    /// <summary>Full text of the page as a single string.</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>Page-level confidence score (0–1), when available.</summary>
    public double? Confidence { get; init; }

    /// <summary>Page width in the provider's unit space (e.g. inches).</summary>
    public double Width { get; init; }

    /// <summary>Page height in the provider's unit space (e.g. inches).</summary>
    public double Height { get; init; }

    public IReadOnlyList<OcrLineResult> Lines { get; init; } = [];

    public IReadOnlyList<OcrWordResult> Words { get; init; } = [];
}
