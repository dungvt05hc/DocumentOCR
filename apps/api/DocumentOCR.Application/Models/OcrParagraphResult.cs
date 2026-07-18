namespace DocumentOCR.Application.Models;

/// <summary>A paragraph detected by the OCR provider's layout analysis.</summary>
public sealed class OcrParagraphResult
{
    public string Content { get; init; } = string.Empty;

    /// <summary>Provider paragraph role (e.g. "title", "sectionHeading", "pageFooter"), when available.</summary>
    public string? Role { get; init; }

    /// <summary>1-based page number, when available.</summary>
    public int? PageNumber { get; init; }

    public BoundingBox? BoundingBox { get; init; }
}
