using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// <see cref="IDocumentOcrProvider"/> backed by a self-hosted, open-source PaddleOCR HTTP
/// service (run separately — see LOCAL_DEVELOPMENT.md — this class only talks to it). Intended
/// as a free baseline for benchmarking against Azure Document Intelligence, not a production
/// replacement: it has no layout/table/key-value-pair support, so <see
/// cref="NormalizedOcrDocument.Tables"/>, <see cref="NormalizedOcrDocument.KeyValuePairs"/> and
/// <see cref="NormalizedOcrDocument.Fields"/> are always empty for this provider.
/// </summary>
public sealed class PaddleOcrProvider : IDocumentOcrProvider
{
    private readonly PaddleOcrOptions _options;
    private readonly ILogger<PaddleOcrProvider> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Lazily-initialized client, mirroring <c>AzureDocumentIntelligenceProvider</c> â€” a missing
    /// BaseUrl must not crash the app when a different provider is selected.
    /// </summary>
    private readonly Lazy<HttpClient?> _client;

    public string ProviderName => "Paddle";

    public PaddleOcrProvider(IOptions<PaddleOcrOptions> options, ILogger<PaddleOcrProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<HttpClient?>(BuildClient, isThreadSafe: true);
    }

    /// <summary>Test-only seam: injects a pre-built <see cref="HttpClient"/> (e.g. wrapping a stub handler) instead of building one from options.</summary>
    internal PaddleOcrProvider(IOptions<PaddleOcrOptions> options, ILogger<PaddleOcrProvider> logger, HttpClient httpClient)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<HttpClient?>(() => httpClient, isThreadSafe: true);
    }

    public async Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
    {
        var client = _client.Value;
        if (client is null)
        {
            const string msg = "PaddleOCR is not configured. Set PaddleOcr__BaseUrl.";
            _logger.LogError(msg);
            return Failure(msg, 0);
        }

        var sw = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        _logger.LogInformation(
            "Starting PaddleOCR analysis. File={FileName} ContentType={ContentType} Size={SizeBytes}B",
            input.FileName, input.ContentType, input.FileSizeBytes);

        try
        {
            using var ms = new MemoryStream();
            await input.Content.CopyToAsync(ms, linkedCts.Token);
            ms.Position = 0;

            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(ms);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(input.ContentType);
            form.Add(fileContent, "file", input.FileName);

            using var response = await client.PostAsync(_options.AnalyzeEndpointPath, form, linkedCts.Token);
            var rawJson = await response.Content.ReadAsStringAsync(linkedCts.Token);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var msg = $"PaddleOCR service returned HTTP {(int)response.StatusCode} ({response.StatusCode}).";
                _logger.LogError("PaddleOCR analysis failed for '{FileName}': {Message}", input.FileName, msg);
                return Failure(msg, sw.Elapsed.TotalMilliseconds, rawJson);
            }

            PaddleOcrResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<PaddleOcrResponse>(rawJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                var msg = $"PaddleOCR returned a response that could not be parsed as JSON: {ex.Message}";
                _logger.LogError(ex, "PaddleOCR analysis returned invalid JSON for '{FileName}'.", input.FileName);
                return Failure(msg, sw.Elapsed.TotalMilliseconds, rawJson);
            }

            if (parsed is null)
            {
                const string msg = "PaddleOCR returned an empty response body.";
                _logger.LogError(msg);
                return Failure(msg, sw.Elapsed.TotalMilliseconds, rawJson);
            }

            if (!parsed.Success)
            {
                var msg = string.IsNullOrWhiteSpace(parsed.ErrorMessage)
                    ? "PaddleOCR reported failure without an error message."
                    : parsed.ErrorMessage;
                _logger.LogError("PaddleOCR analysis failed for '{FileName}': {Message}", input.FileName, msg);
                return Failure(msg, sw.Elapsed.TotalMilliseconds, rawJson);
            }

            var pages = (parsed.Pages ?? []).Select(BuildPage).ToList();
            var fullText = string.IsNullOrWhiteSpace(parsed.FullText)
                ? string.Join("\n", pages.Select(p => p.FullText))
                : parsed.FullText;

            _logger.LogInformation(
                "PaddleOCR analysis completed. File={FileName} Pages={PageCount} Elapsed={ElapsedMs}ms",
                input.FileName, pages.Count, sw.Elapsed.TotalMilliseconds);

            return new NormalizedOcrDocument
            {
                Success = true,
                ProviderName = ProviderName,
                ModelId = "paddleocr",
                FullText = fullText,
                Pages = pages,
                AverageConfidence = parsed.AverageConfidence ?? CalculateAverageConfidence(pages),
                PageCount = parsed.PageCount > 0 ? parsed.PageCount : pages.Count,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
                EstimatedCost = 0m,
                RawProviderResponseJson = rawJson
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            var msg = $"PaddleOCR analysis timed out after {_options.TimeoutSeconds}s for '{input.FileName}'.";
            _logger.LogError(msg);
            return Failure(msg, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("PaddleOCR analysis cancelled for '{FileName}'.", input.FileName);
            return Failure("Analysis was cancelled.", sw.Elapsed.TotalMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            var msg = $"PaddleOCR service is unreachable at '{_options.BaseUrl}': {ex.Message}";
            _logger.LogError(ex, "PaddleOCR analysis failed for '{FileName}': service unreachable.", input.FileName);
            return Failure(msg, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected error during PaddleOCR analysis for '{FileName}'.", input.FileName);
            return Failure($"Unexpected error: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static OcrPage BuildPage(PaddleOcrPage page)
    {
        var lines = (page.Lines ?? []).Select(BuildLine).ToList();

        return new OcrPage
        {
            PageNumber = page.PageNumber,
            FullText = string.Join(" ", lines.Select(l => l.Text)),
            Confidence = lines.Count > 0 ? lines.Average(l => l.Confidence ?? 0) : null,
            Width = page.Width ?? 0,
            Height = page.Height ?? 0,
            Unit = page.Unit,
            Lines = lines,
            Words = lines.SelectMany(l => l.Words).ToList()
        };
    }

    private static OcrLine BuildLine(PaddleOcrLine line)
    {
        var words = (line.Words ?? []).Select(BuildWord).ToList();

        return new OcrLine
        {
            Text = line.Text,
            Confidence = line.Confidence,
            BoundingBox = ToBoundingBox(line.BoundingBox),
            Words = words
        };
    }

    private static OcrWord BuildWord(PaddleOcrWord word) => new()
    {
        Text = word.Text,
        Confidence = word.Confidence,
        BoundingBox = ToBoundingBox(word.BoundingBox)
    };

    private static BoundingBox? ToBoundingBox(List<List<double>>? points)
    {
        if (points is not { Count: > 0 }) return null;

        var boundingPoints = points
            .Where(p => p.Count >= 2)
            .Select(p => new BoundingPoint(p[0], p[1]))
            .ToList();

        return boundingPoints.Count > 0 ? new BoundingBox(boundingPoints) : null;
    }

    private static double? CalculateAverageConfidence(IReadOnlyList<OcrPage> pages)
    {
        var confidences = pages
            .SelectMany(p => p.Lines)
            .Where(l => l.Confidence.HasValue)
            .Select(l => l.Confidence!.Value)
            .ToList();

        return confidences.Count > 0 ? confidences.Average() : null;
    }

    private HttpClient? BuildClient()
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning(
                "PaddleOCR is not configured. Set PaddleOcr:BaseUrl to enable this provider.");
            return null;
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            _logger.LogError("PaddleOcr:BaseUrl '{BaseUrl}' is not a valid URI.", _options.BaseUrl);
            return null;
        }

        _logger.LogDebug(
            "PaddleOCR client initialized. BaseUrl={BaseUrl} Timeout={TimeoutSeconds}s",
            _options.BaseUrl, _options.TimeoutSeconds);

        // Timeout is enforced per-call via the linked CancellationTokenSource above (consistent
        // with AzureDocumentIntelligenceProvider), not via HttpClient.Timeout, so options changes
        // don't require rebuilding the client.
        return new HttpClient { BaseAddress = baseUri };
    }

    private NormalizedOcrDocument Failure(string message, double processingTimeMs, string? rawResponseJson = null) => new()
    {
        Success = false,
        ProviderName = ProviderName,
        ErrorMessage = message,
        ModelId = "paddleocr",
        ProcessingTimeMs = processingTimeMs,
        RawProviderResponseJson = rawResponseJson
    };
}
