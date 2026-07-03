namespace DocumentOCR.Application.Models;

/// <summary>A single line of text recognized by the OCR provider.</summary>
public sealed class OcrLineResult
{
    public int LineNumber { get; init; }
    public string Text { get; init; } = string.Empty;
    public double? Confidence { get; init; }
    public BoundingBox? BoundingBox { get; init; }
    public IReadOnlyList<OcrWordResult> Words { get; init; } = [];
}
