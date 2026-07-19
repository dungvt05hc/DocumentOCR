namespace DocumentOCR.Application.Models;

/// <summary>A single line of text recognized by the OCR provider.</summary>
public sealed class OcrLine
{
    public int LineNumber { get; init; }
    public string Text { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public double? Confidence { get; init; }
    public BoundingBox? BoundingBox { get; init; }

    /// <summary>Offset of this line's text within the page/document's full text, when available.</summary>
    public int? SpanOffset { get; init; }

    /// <summary>Length of this line's text span, when available.</summary>
    public int? SpanLength { get; init; }

    public IReadOnlyList<OcrWord> Words { get; init; } = [];
}
