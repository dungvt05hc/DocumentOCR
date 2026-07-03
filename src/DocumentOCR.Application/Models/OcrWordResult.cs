namespace DocumentOCR.Application.Models;

/// <summary>An individual word recognized by the OCR provider.</summary>
public sealed class OcrWordResult
{
    public string Text { get; init; } = string.Empty;
    public double? Confidence { get; init; }
    public BoundingBox? BoundingBox { get; init; }
}
