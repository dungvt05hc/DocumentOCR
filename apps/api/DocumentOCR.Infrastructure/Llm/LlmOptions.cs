namespace DocumentOCR.Infrastructure.Llm;

/// <summary>
/// Which Gemini usage tier the configured API key is on. Purely informational — it does not
/// change request behavior — used only to decide whether to log the free-tier data-usage warning
/// at startup (see <see cref="LlmStartupWarningService"/>).
/// </summary>
public enum LlmTier
{
    Free,
    Paid
}

/// <summary>Configuration for the text-only LLM field-extraction path (see <c>PdfTextLayerLlmStrategy</c>).</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// Master switch for the whole PDF-text-layer+LLM strategy. Defaults to <see langword="false"/>
    /// so this path never runs (and never costs money) until explicitly turned on — PDFs keep going
    /// through the existing free text-layer heuristic / OCR path until then.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Which LLM provider to use. Only "Gemini" is implemented today.</summary>
    public string Provider { get; set; } = "Gemini";

    /// <summary>
    /// Provider-specific model identifier. Defaults to "gemini-3.5-flash-lite" — never
    /// "gemini-2.5-flash-lite" (Google ends support for it 2026-10-16). If 3.5-flash-lite isn't
    /// available for your API key/region, set <c>Llm:Model</c> to "gemini-3.1-flash-lite" instead.
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>
    /// Which usage tier <see cref="ApiKey"/> is on. Defaults to <see cref="LlmTier.Free"/> — the
    /// safer assumption, since a free-tier key means the provider may use submitted content to
    /// improve their product. Set to <see cref="LlmTier.Paid"/> once billing is enabled to silence
    /// the startup warning.
    /// </summary>
    public LlmTier Tier { get; set; } = LlmTier.Free;

    /// <summary>
    /// Maximum number of concurrent Gemini requests across all in-flight document processing jobs.
    /// Keeps a batch of documents from blowing through the free tier's ~5-15 requests/minute limit
    /// all at once. Default: 2.
    /// </summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>
    /// Backoff delays (seconds) between retries after an HTTP 429 (rate limited) response — one
    /// retry per entry, in order. Default: 2s, 5s, 10s (3 retries). After the last retry still
    /// returns 429, <see cref="GeminiExtractionClient"/> throws so the caller
    /// (<c>PdfTextLayerLlmStrategy</c>) falls through to <c>OcrStrategy</c> instead of leaving the
    /// document stuck in <c>Processing</c>.
    /// </summary>
    public double[] RetryDelaysSeconds { get; set; } = [2, 5, 10];

    /// <summary>API key — from user-secrets or the <c>Llm__ApiKey</c> environment variable. Never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Total timeout for one extraction call, in seconds. Default: 60.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of characters of extracted PDF text sent to the LLM. Longer input is
    /// truncated (with a logged Warning) rather than rejected outright. Default: 20000.
    /// </summary>
    public int MaxInputChars { get; set; } = 20000;

    /// <summary>
    /// USD price per 1,000,000 input/output tokens for whatever model <see cref="Model"/> names —
    /// token pricing varies by model and changes over time, so this is deliberately left at 0
    /// (EstimatedCost reports 0, not a guessed figure) until set to the real published rate for the
    /// configured model.
    /// </summary>
    public decimal PricePerMillionInputTokensUsd { get; set; }

    public decimal PricePerMillionOutputTokensUsd { get; set; }

    /// <summary>True when the minimum required configuration (model + API key) is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);
}
