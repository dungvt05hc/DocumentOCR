using System.Diagnostics;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// <see cref="IDocumentOcrProvider"/> backed by Azure Document Intelligence.
/// <para>
/// Credentials are read from <see cref="AzureOcrOptions"/>; never hard-code values.
/// This class is registered as a <b>Singleton</b> â€” the underlying client is thread-safe
/// and designed to be reused.
/// </para>
/// <para>
/// Transient failure retry (HTTP 429, 503, network errors) is handled by the Azure SDK's
/// built-in retry pipeline, configured via <see cref="AzureOcrOptions.MaxRetries"/> and
/// <see cref="AzureOcrOptions.RetryDelaySeconds"/>.
/// </para>
/// </summary>
public sealed class AzureDocumentIntelligenceProvider : IDocumentOcrProvider
{
    private readonly AzureOcrOptions _options;
    private readonly ILogger<AzureDocumentIntelligenceProvider> _logger;

    /// <summary>
    /// Lazily-initialized client. Deferred so that a missing/invalid endpoint at startup
    /// does not crash the application when using a different provider (e.g. FakeOcrProvider).
    /// </summary>
    private readonly Lazy<DocumentIntelligenceClient?> _client;

    public string ProviderName => "AzureDocumentIntelligence";

    public AzureDocumentIntelligenceProvider(
        IOptions<AzureOcrOptions> options,
        ILogger<AzureDocumentIntelligenceProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<DocumentIntelligenceClient?>(BuildClient, isThreadSafe: true);
    }

    // â”€â”€ IDocumentOcrProvider â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public async Task<OcrResult> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
    {
        var client = _client.Value;
        if (client is null)
        {
            const string msg = "Azure Document Intelligence is not configured. " +
                               "Set AzureDocumentIntelligence__Endpoint and AzureDocumentIntelligence__ApiKey.";
            _logger.LogError(msg);
            return Failure(msg, 0);
        }

        var sw = Stopwatch.StartNew();

        // â”€â”€ Combined cancellation: caller token + operation timeout â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        _logger.LogInformation(
            "Starting OCR analysis. File={FileName} ContentType={ContentType} Size={SizeBytes}B Model={ModelId}",
            input.FileName, input.ContentType, input.FileSizeBytes, _options.DefaultModelId);

        try
        {
            // â”€â”€ Read stream into memory (Azure SDK requires BinaryData) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            using var ms = new MemoryStream();
            await input.Content.CopyToAsync(ms, linkedCts.Token);

            var binaryData = BinaryData.FromBytes(ms.ToArray());
            var analyzeOptions = new AnalyzeDocumentOptions(_options.DefaultModelId, binaryData);

            var operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                analyzeOptions,
                cancellationToken: linkedCts.Token);

            sw.Stop();

            var analyzeResult = operation.Value;

            // â”€â”€ Capture raw HTTP response body for debugging â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var rawJson = TryCaptureRawJson(operation);

            // â”€â”€ Extract key-value fields â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var fieldCandidates = ExtractFieldCandidates(analyzeResult);

            // â”€â”€ Build per-page results â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var pageResults = analyzeResult.Pages
                .Select(BuildPageResult)
                .ToList();

            var fullText = string.Join("\n", pageResults.Select(p => p.FullText));
            var pageCount = analyzeResult.Pages.Count;

            _logger.LogInformation(
                "OCR analysis completed. File={FileName} Pages={PageCount} Fields={FieldCount} Elapsed={ElapsedMs}ms",
                input.FileName, pageCount, fieldCandidates.Count, sw.Elapsed.TotalMilliseconds);

            return new OcrResult
            {
                Success = true,
                FullText = fullText,
                Pages = pageResults,
                Fields = fieldCandidates,
                PageCount = pageCount,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
                EstimatedCost = CalculateCost(pageCount),
                ModelId = _options.DefaultModelId,
                RawProviderResponseJson = rawJson
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            var msg = $"OCR analysis timed out after {_options.OperationTimeoutSeconds}s for '{input.FileName}'.";
            _logger.LogError(msg);
            return Failure(msg, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("OCR analysis cancelled for '{FileName}'.", input.FileName);
            return Failure("Analysis was cancelled.", sw.Elapsed.TotalMilliseconds);
        }
        catch (RequestFailedException rfx) when (rfx.Status is 401 or 403)
        {
            sw.Stop();
            var msg = $"Azure authentication failed (HTTP {rfx.Status}). Check Endpoint and ApiKey configuration.";
            _logger.LogError(rfx, msg);
            return Failure(msg, sw.Elapsed.TotalMilliseconds);
        }
        catch (RequestFailedException rfx) when (rfx.Status == 400)
        {
            sw.Stop();
            var msg = $"Azure rejected the request (HTTP 400): {rfx.Message}. " +
                      $"Verify the model '{_options.DefaultModelId}' supports content type '{input.ContentType}'.";
            _logger.LogError(rfx, msg);
            return Failure(msg, sw.Elapsed.TotalMilliseconds);
        }
        catch (RequestFailedException rfx)
        {
            // Transient errors (429, 503, etc.) are already retried by the SDK;
            // if we reach here all retries were exhausted.
            sw.Stop();
            var msg = $"Azure Document Intelligence request failed after retries (HTTP {rfx.Status}): {rfx.Message}";
            _logger.LogError(rfx, "OCR analysis failed for '{FileName}' after retries.", input.FileName);
            return Failure(msg, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected error during OCR analysis for '{FileName}'.", input.FileName);
            return Failure($"Unexpected error: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    // â”€â”€ Client factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private DocumentIntelligenceClient? BuildClient()
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning(
                "Azure Document Intelligence is not configured. " +
                "Set AzureDocumentIntelligence__Endpoint and AzureDocumentIntelligence__ApiKey " +
                "to enable this provider.");
            return null;
        }

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            _logger.LogError(
                "AzureDocumentIntelligence:Endpoint '{Endpoint}' is not a valid URI.",
                _options.Endpoint);
            return null;
        }

        var clientOptions = new DocumentIntelligenceClientOptions
        {
            Retry =
            {
                MaxRetries = _options.MaxRetries,
                Mode = RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(_options.RetryDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(Math.Max(30, _options.RetryDelaySeconds * 16)),
                NetworkTimeout = TimeSpan.FromSeconds(_options.NetworkTimeoutSeconds)
            }
        };

        _logger.LogDebug(
            "Azure Document Intelligence client initialized. Endpoint={Endpoint} Model={ModelId} " +
            "MaxRetries={MaxRetries} NetworkTimeout={NetworkTimeoutSeconds}s OperationTimeout={OperationTimeoutSeconds}s",
            _options.Endpoint, _options.DefaultModelId,
            _options.MaxRetries, _options.NetworkTimeoutSeconds, _options.OperationTimeoutSeconds);

        return new DocumentIntelligenceClient(
            endpointUri,
            new AzureKeyCredential(_options.ApiKey),
            clientOptions);
    }

    // â”€â”€ Field extraction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<OcrFieldCandidate> ExtractFieldCandidates(AnalyzeResult analyzeResult)
    {
        var candidates = new List<OcrFieldCandidate>();

        if (analyzeResult.Documents is not { Count: > 0 })
            return candidates;

        foreach (var kvp in analyzeResult.Documents[0].Fields)
        {
            var field = kvp.Value;
            BoundingBox? bb = null;
            int? pageNumber = null;

            if (field.BoundingRegions is { Count: > 0 })
            {
                pageNumber = field.BoundingRegions[0].PageNumber;
                bb = ToPolygonBoundingBox(field.BoundingRegions[0].Polygon);
            }

            candidates.Add(new OcrFieldCandidate
            {
                FieldKey = kvp.Key,
                Value = field.Content,
                Confidence = field.Confidence.HasValue ? (double)field.Confidence.Value : null,
                PageNumber = pageNumber,
                BoundingBox = bb,
                RawProviderKey = kvp.Key
            });
        }

        return candidates;
    }

    // â”€â”€ Page result builder â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static OcrPageResult BuildPageResult(DocumentPage page)
    {
        var lines = page.Lines?
            .Select((line, i) =>
            {
                var words = page.Words?
                    .Where(w => IsWordOnLine(w, line))
                    .Select(w => new OcrWordResult
                    {
                        Text = w.Content,
                        Confidence = (double)w.Confidence,
                        BoundingBox = ToPolygonBoundingBox(w.Polygon)
                    })
                    .ToList() ?? [];

                return new OcrLineResult
                {
                    LineNumber = i + 1,
                    Text = line.Content,
                    BoundingBox = ToPolygonBoundingBox(line.Polygon),
                    Words = words
                };
            })
            .ToList() ?? [];

        var pageWords = page.Words?
            .Select(w => new OcrWordResult
            {
                Text = w.Content,
                Confidence = (double)w.Confidence,
                BoundingBox = ToPolygonBoundingBox(w.Polygon)
            })
            .ToList() ?? [];

        return new OcrPageResult
        {
            PageNumber = page.PageNumber,
            FullText = string.Join(" ", page.Lines?.Select(l => l.Content) ?? []),
            Width = page.Width.HasValue ? (double)page.Width.Value : 0,
            Height = page.Height.HasValue ? (double)page.Height.Value : 0,
            Lines = lines,
            Words = pageWords
        };
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Checks whether a word polygon overlaps the line polygon using a Y-centroid heuristic.
    /// </summary>
    private static bool IsWordOnLine(DocumentWord word, DocumentLine line)
    {
        if (word.Polygon is not { Count: >= 2 } || line.Polygon is not { Count: >= 2 })
            return false;

        var wordY = word.Polygon.Where((_, i) => i % 2 == 1).Average();
        var lineMinY = line.Polygon.Where((_, i) => i % 2 == 1).Min();
        var lineMaxY = line.Polygon.Where((_, i) => i % 2 == 1).Max();

        return wordY >= lineMinY && wordY <= lineMaxY;
    }

    private static BoundingBox? ToPolygonBoundingBox(IReadOnlyList<float>? polygon)
    {
        if (polygon is not { Count: >= 2 }) return null;

        var points = new List<BoundingPoint>();
        for (var i = 0; i + 1 < polygon.Count; i += 2)
            points.Add(new BoundingPoint(polygon[i], polygon[i + 1]));

        return new BoundingBox(points);
    }

    /// <summary>
    /// Captures the raw HTTP response body from the completed polling operation.
    /// This is the actual JSON returned by Azure â€” more reliable than re-serializing typed objects.
    /// Returns <see langword="null"/> on any failure (non-critical, debug-only data).
    /// </summary>
    private static string? TryCaptureRawJson(Operation<AnalyzeResult> operation)
    {
        try
        {
            return operation.GetRawResponse()?.Content.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rough per-page cost estimate. Update pricing tiers as Azure billing changes.
    /// </summary>
    private static decimal CalculateCost(int pageCount)
    {
        // Prebuilt models: ~$0.01 per page (Azure 2024 pricing â€” verify before using in billing)
        const decimal pricePerPage = 0.01m;
        return pageCount * pricePerPage;
    }

    private OcrResult Failure(string message, double processingTimeMs) => new()
    {
        Success = false,
        ErrorMessage = message,
        ProcessingTimeMs = processingTimeMs,
        ModelId = _options.DefaultModelId
    };
}
