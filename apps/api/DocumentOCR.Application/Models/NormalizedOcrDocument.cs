namespace DocumentOCR.Application.Models;

/// <summary>
/// Provider-neutral, normalized OCR output. Every <see cref="Interfaces.IDocumentOcrProvider"/>
/// implementation maps its vendor-specific response into this shape — field extraction,
/// normalization, and validation never see vendor SDK types.
/// </summary>
public sealed record NormalizedOcrDocument
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Human-readable provider identifier (e.g. "AzureDocumentIntelligence", "Fake").</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>Provider-specific model identifier used for this analysis (e.g. "prebuilt-invoice", "Fake").</summary>
    public string? ModelId { get; init; }

    /// <summary>Provider API version used for this analysis, when available.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Add-on features that were requested for this analysis (e.g. "keyValuePairs").</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Full raw text from all pages concatenated with newlines.</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>Document-level average confidence (0–1), when computable.</summary>
    public double? AverageConfidence { get; init; }

    /// <summary>Per-page structured OCR data (lines, words, spatial layout).</summary>
    public IReadOnlyList<OcrPage> Pages { get; init; } = [];

    /// <summary>Structured key-value fields extracted across all pages (prebuilt-invoice/receipt style).</summary>
    public IReadOnlyList<OcrFieldCandidate> Fields { get; init; } = [];

    /// <summary>Paragraphs detected by the provider's layout analysis, with roles when available.</summary>
    public IReadOnlyList<OcrParagraph> Paragraphs { get; init; } = [];

    /// <summary>Tables detected by the provider's layout analysis.</summary>
    public IReadOnlyList<OcrTable> Tables { get; init; } = [];

    /// <summary>Generic key-value pairs from the provider's "keyValuePairs" add-on feature, when enabled.</summary>
    public IReadOnlyList<OcrKeyValuePair> KeyValuePairs { get; init; } = [];

    public int PageCount { get; init; }
    public double ProcessingTimeMs { get; init; }
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Raw provider response serialized as JSON — kept for debugging and auditability.
    /// Do NOT use for business logic; parse the structured fields above instead.
    /// </summary>
    public string? RawProviderResponseJson { get; init; }

    /// <summary>Storage path of the persisted raw provider response, when <c>Ocr:StoreRawProviderResponse</c> is enabled.</summary>
    public string? RawProviderResponsePath { get; init; }

    /// <summary>Storage path of the persisted normalized OCR result, when <c>Ocr:StoreNormalizedOcrResult</c> is enabled.</summary>
    public string? NormalizedOcrResultPath { get; init; }

    // ── Convenience projections ─────────────────────────────────────────────────

    /// <summary>Per-page full-text strings derived from <see cref="Pages"/>.</summary>
    public IReadOnlyList<string> PageTexts => Pages.Select(p => p.FullText).ToList();

    /// <summary>All lines across all pages, in page order.</summary>
    public IReadOnlyList<OcrLine> Lines => Pages.SelectMany(p => p.Lines).ToList();

    /// <summary>All words across all pages, in page order.</summary>
    public IReadOnlyList<OcrWord> Words => Pages.SelectMany(p => p.Words).ToList();
}
