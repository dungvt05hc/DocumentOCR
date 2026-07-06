namespace DocumentOCR.Application.Models;

/// <summary>Single field result as returned by the OCR provider.</summary>
public class OcrFieldResult
{
    public string FieldKey { get; set; } = string.Empty;
    public string? Value { get; set; }
    public double? Confidence { get; set; }
}
