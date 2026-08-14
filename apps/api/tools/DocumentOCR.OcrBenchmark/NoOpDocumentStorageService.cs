using DocumentOCR.Application.Interfaces;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// <see cref="IDocumentStorageService"/> that never persists — this dev-only tool already writes
/// its own per-target debug JSON files (raw-response.json, ocr-result.json, ...) directly to the
/// benchmark output folder, so <see cref="PdfTextLayerLlmStrategy"/>'s raw-response artifact write
/// (used in production to back the ocr-debug endpoint) has nothing useful to do here.
/// </summary>
public sealed class NoOpDocumentStorageService : IDocumentStorageService
{
    public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default) =>
        Task.FromResult(string.Empty);

    public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by the benchmark tool.");

    public Task DeleteAsync(string storedPath, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by the benchmark tool.");
}
