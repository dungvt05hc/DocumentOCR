namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>Top-level OCR pipeline configuration — which provider to use and how to log it.</summary>
public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    /// <summary>Which <see cref="Application.Interfaces.IDocumentOcrProvider"/> to register: "Fake" or "Azure".</summary>
    public string Provider { get; set; } = "Fake";

    /// <summary>
    /// Whether to persist <c>OcrResult.RawProviderResponseJson</c> into <c>OcrProviderLog.RawResponseJson</c>.
    /// Defaults to true (useful for debugging field-mapping issues); set false to reduce DB row size
    /// once the Azure integration is trusted.
    /// </summary>
    public bool StoreRawProviderResponse { get; set; } = true;
}
