namespace DocumentOCR.Application.Models;

/// <summary>An individual word recognized by the OCR provider.</summary>
public sealed class OcrWord
{
    public string Text { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public double? Confidence { get; init; }
    public BoundingBox? BoundingBox { get; init; }

    /// <summary>Offset of this word's text within the page/document's full text, when available.</summary>
    public int? SpanOffset { get; init; }

    /// <summary>Length of this word's text span, when available.</summary>
    public int? SpanLength { get; init; }
}
