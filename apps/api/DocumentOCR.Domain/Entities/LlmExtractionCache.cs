using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

/// <summary>
/// Caches one <see cref="ExtractedField"/>-source LLM response (see <c>ILlmExtractionClient</c>)
/// keyed by (<see cref="TextHash"/>, <see cref="Model"/>) so re-processing the same PDF text under
/// the same model never re-calls a paid LLM. A cache hit still goes through the full
/// anti-hallucination/confidence pipeline in <c>PdfTextLayerLlmStrategy</c> — this only saves the
/// network call, not the verification.
/// </summary>
public class LlmExtractionCache : BaseEntity
{
    /// <summary>SHA-256 hex digest of the whitespace-normalized extracted PDF text.</summary>
    public string TextHash { get; set; } = string.Empty;

    /// <summary>Provider model identifier the response was generated with (e.g. "gemini-2.5-flash").</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Serialized <c>LlmExtractionResult</c> JSON.</summary>
    public string ResponseJson { get; set; } = string.Empty;
}
