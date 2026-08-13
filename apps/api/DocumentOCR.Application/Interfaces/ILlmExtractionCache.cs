namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Caches <see cref="ILlmExtractionClient"/> responses keyed by (text hash, model) so
/// re-processing the same PDF text under the same model never re-calls a paid LLM. A cache hit
/// must still go through the caller's full anti-hallucination/confidence pipeline — this only
/// saves the network call, not the verification.
/// </summary>
public interface ILlmExtractionCache
{
    Task<string?> TryGetResponseJsonAsync(string textHash, string model, CancellationToken ct = default);

    Task SetAsync(string textHash, string model, string responseJson, CancellationToken ct = default);
}
