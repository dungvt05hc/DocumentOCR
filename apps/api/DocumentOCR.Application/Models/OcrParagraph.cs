namespace DocumentOCR.Application.Models;

/// <summary>A paragraph detected by the OCR provider's layout analysis.</summary>
public sealed class OcrParagraph
{
    public string Text { get; init; } = string.Empty;

    /// <summary>Provider paragraph role (e.g. "title", "sectionHeading", "pageFooter"), when available.</summary>
    public string? Role { get; init; }

    /// <summary>1-based page number, when available.</summary>
    public int? PageNumber { get; init; }

    public BoundingBox? BoundingBox { get; init; }

    /// <summary>Offset of this paragraph's text within the document's full text, when available.</summary>
    public int? SpanOffset { get; init; }

    /// <summary>Length of this paragraph's text span, when available.</summary>
    public int? SpanLength { get; init; }
}
