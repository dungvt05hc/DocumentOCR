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

    /// <summary>
    /// Whether to serialize the full <c>NormalizedOcrDocument</c> to a JSON file via
    /// <see cref="Application.Interfaces.IDocumentStorageService"/> and record its path on
    /// <c>OcrProviderLog.NormalizedResultPath</c>. Defaults to true (useful for debugging
    /// extraction issues); set false to reduce storage usage once the pipeline is trusted.
    /// </summary>
    public bool StoreNormalizedOcrResult { get; set; } = true;

    /// <summary>
    /// Hard ceiling, in seconds, on a single <see cref="Application.Interfaces.IDocumentOcrProvider.AnalyzeAsync"/>
    /// call from <c>DocumentProcessingService</c>. Enforced at the pipeline level (not inside each
    /// provider) so a provider that hangs — or a future provider that forgets its own timeout —
    /// can never leave a document stuck in <c>Processing</c> forever; on expiry the document is
    /// marked <c>Failed</c> with a clear message instead. Azure/Paddle already enforce their own,
    /// shorter, provider-specific timeouts (<c>AzureOcrOptions.OperationTimeoutSeconds</c>,
    /// <c>PaddleOcrOptions.TimeoutSeconds</c>) — this is the outer backstop. Default: 120.
    /// </summary>
    public int CallTimeoutSeconds { get; set; } = 120;
}
