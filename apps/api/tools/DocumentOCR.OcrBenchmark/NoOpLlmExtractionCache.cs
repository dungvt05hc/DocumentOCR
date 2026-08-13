using DocumentOCR.Application.Interfaces;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// <see cref="ILlmExtractionCache"/> that never caches — this dev-only tool has no database, and a
/// benchmark run's whole point is to measure real per-call cost/latency, not to hide it behind a
/// cache hit.
/// </summary>
public sealed class NoOpLlmExtractionCache : ILlmExtractionCache
{
    public Task<string?> TryGetResponseJsonAsync(string textHash, string model, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task SetAsync(string textHash, string model, string responseJson, CancellationToken ct = default) =>
        Task.CompletedTask;
}
