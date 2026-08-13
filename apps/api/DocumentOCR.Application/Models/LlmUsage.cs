namespace DocumentOCR.Application.Models;

/// <summary>
/// Token/cost accounting for one <see cref="Interfaces.ILlmExtractionClient.ExtractAsync"/> call —
/// populated by the client from the provider's own usage metadata after the response comes back,
/// never part of the JSON schema the model is asked to produce.
/// </summary>
public sealed record LlmUsage(int InputTokens, int OutputTokens, decimal EstimatedCostUsd);
