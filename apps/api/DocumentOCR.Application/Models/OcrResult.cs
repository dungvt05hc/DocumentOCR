namespace DocumentOCR.Application.Models;

/// <summary>Vendor-neutral result returned by any <see cref="Interfaces.IDocumentOcrProvider"/> implementation.</summary>
public sealed class OcrResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Full raw text from all pages concatenated with newlines.</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>Per-page structured OCR data (lines, words, spatial layout).</summary>
    public IReadOnlyList<OcrPageResult> Pages { get; init; } = [];

    /// <summary>Document-level average confidence (0–1), when provided by the vendor.</summary>
    public double? Confidence { get; init; }

    /// <summary>Structured key-value fields extracted across all pages.</summary>
    public IReadOnlyList<OcrFieldCandidate> Fields { get; init; } = [];

    public int PageCount { get; init; }
    public double ProcessingTimeMs { get; init; }
    public decimal EstimatedCost { get; init; }

    /// <summary>Provider-specific model identifier used for this analysis (e.g. "prebuilt-invoice", "Fake").</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Raw provider response serialized as JSON — kept for debugging and auditability.
    /// Do NOT use for business logic; parse the structured fields above instead.
    /// </summary>
    public string? RawProviderResponseJson { get; init; }

    // ── Backward-compat helpers used by processing pipeline ────────────────────────

    /// <summary>Per-page full-text strings derived from <see cref="Pages"/>.</summary>
    public IReadOnlyList<string> PageTexts =>
        Pages.Select(p => p.FullText).ToList();
}
