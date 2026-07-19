namespace DocumentOCR.Application.Models;

/// <summary>OCR results for a single page of the document.</summary>
public sealed class OcrPage
{
    /// <summary>1-based page number.</summary>
    public int PageNumber { get; init; }

    /// <summary>Full text of the page as a single string.</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>Page-level confidence score (0–1), when available.</summary>
    public double? Confidence { get; init; }

    /// <summary>Page width in <see cref="Unit"/> space.</summary>
    public double Width { get; init; }

    /// <summary>Page height in <see cref="Unit"/> space.</summary>
    public double Height { get; init; }

    /// <summary>Unit for <see cref="Width"/>/<see cref="Height"/> (e.g. "inch", "pixel"), when known.</summary>
    public string? Unit { get; init; }

    /// <summary>Detected text angle in degrees, when available.</summary>
    public double? Angle { get; init; }

    public IReadOnlyList<OcrLine> Lines { get; init; } = [];

    public IReadOnlyList<OcrWord> Words { get; init; } = [];
}
