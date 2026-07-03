namespace DocumentOCR.Infrastructure.Ocr;

public sealed class AzureOcrOptions
{
    public const string SectionName = "AzureDocumentIntelligence";

    // ── Credentials (set via environment variables — never commit values) ─────────

    /// <summary>Azure Document Intelligence endpoint URL (e.g. https://&lt;name&gt;.cognitiveservices.azure.com/).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure Document Intelligence API key. Always set via environment variable in production.</summary>
    public string ApiKey { get; set; } = string.Empty;

    // ── Model selection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Model ID applied to all documents unless overridden. Supported values:
    /// prebuilt-read, prebuilt-layout, prebuilt-invoice, prebuilt-receipt.
    /// </summary>
    public string DefaultModelId { get; set; } = "prebuilt-invoice";

    // ── Resilience ────────────────────────────────────────────────────────────────

    /// <summary>Per-HTTP-request network timeout in seconds. Default: 30.</summary>
    public int NetworkTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Total timeout for a complete AnalyzeDocument operation (including polling) in seconds.
    /// Default: 120.
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum number of retries for transient failures (429, 503, network). Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Initial delay between retries in seconds (exponential back-off). Default: 1.</summary>
    public double RetryDelaySeconds { get; set; } = 1.0;

    // ── Computed ──────────────────────────────────────────────────────────────────

    /// <summary>True when the minimum required credentials are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}

