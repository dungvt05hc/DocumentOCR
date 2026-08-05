namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>Configuration for the text-layer-first PDF path (<see cref="PdfTextLayerProvider"/>/<see cref="PdfProviderRouter"/>).</summary>
public sealed class PdfTextLayerOptions
{
    public const string SectionName = "Ocr:PdfTextLayer";

    /// <summary>
    /// When true (default), <see cref="PdfProviderRouter"/> tries <see cref="PdfTextLayerProvider"/>
    /// first for PDF uploads before falling back to the configured OCR provider. Set false to force
    /// every PDF through OCR — e.g. to compare extraction quality between the two paths.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum total extracted character count (trimmed, across all pages) below which a PDF is
    /// treated as scanned (no usable text layer) rather than software-generated.
    /// </summary>
    public int MinExtractedCharacters { get; set; } = 100;
}
