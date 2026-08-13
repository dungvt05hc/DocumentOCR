namespace DocumentOCR.Infrastructure.Llm;

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

    /// <summary>Provider-specific model identifier (e.g. "gemini-2.5-flash").</summary>
    public string Model { get; set; } = string.Empty;

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
