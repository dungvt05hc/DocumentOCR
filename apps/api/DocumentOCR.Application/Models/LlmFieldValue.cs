using System.Text.Json.Serialization;

namespace DocumentOCR.Application.Models;

/// <summary>
/// One field as reported by <see cref="Interfaces.ILlmExtractionClient"/>: the LLM must copy
/// <see cref="Value"/> verbatim from <see cref="SourceText"/> (no calculation, no reformatting) —
/// enforced downstream by the caller re-checking <see cref="SourceText"/> actually occurs in the
/// source document text before trusting <see cref="Value"/>. Both null means the field wasn't
/// found — never a guessed or zeroed value.
/// </summary>
public sealed record LlmFieldValue(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("sourceText")] string? SourceText);
