using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Application.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Llm;

/// <summary>
/// <see cref="ILlmExtractionClient"/> backed by the Gemini API's <c>generateContent</c> endpoint,
/// using its native <c>responseSchema</c> structured-output mechanism (never a "reply with JSON"
/// prompt parsed by hand) with <c>temperature = 0</c>. Gemini SDK/HTTP shape never leaves this
/// class — callers only see <see cref="LlmExtractionResult"/>.
/// </summary>
public sealed class GeminiExtractionClient : ILlmExtractionClient
{
    private const string ApiBaseUrl = "https://generativelanguage.googleapis.com/";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LlmOptions _options;
    private readonly ILogger<GeminiExtractionClient> _logger;

    /// <summary>Lazily-initialized client — a missing API key must not crash the app when the LLM path is disabled.</summary>
    private readonly Lazy<HttpClient?> _client;

    public GeminiExtractionClient(IOptions<LlmOptions> options, ILogger<GeminiExtractionClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<HttpClient?>(BuildClient, isThreadSafe: true);
    }

    /// <summary>Test-only seam: injects a pre-built <see cref="HttpClient"/> (wrapping a stub handler) instead of building one from options.</summary>
    internal GeminiExtractionClient(IOptions<LlmOptions> options, ILogger<GeminiExtractionClient> logger, HttpClient httpClient)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<HttpClient?>(() => httpClient, isThreadSafe: true);
    }

    public async Task<LlmExtractionResult> ExtractAsync(string documentText, CancellationToken ct = default)
    {
        var client = _client.Value
            ?? throw new InvalidOperationException("Gemini is not configured. Set Llm:Model and Llm:ApiKey.");

        var truncatedText = TruncateIfNeeded(documentText);
        var requestBody = BuildRequestBody(truncatedText);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var sw = Stopwatch.StartNew();
        var url = $"v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}";

        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(url, content, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Gemini extraction call timed out after {_options.TimeoutSeconds}s.");
        }

        using (response)
        {
            var rawJson = await response.Content.ReadAsStringAsync(linkedCts.Token);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Gemini API returned HTTP {StatusCode} in {ElapsedMs}ms.",
                    (int)response.StatusCode, sw.Elapsed.TotalMilliseconds);
                throw new InvalidOperationException($"Gemini API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            using var responseDoc = JsonDocument.Parse(rawJson);
            var root = responseDoc.RootElement;

            var candidateText = root
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(candidateText))
                throw new InvalidOperationException("Gemini response did not contain any candidate text.");

            var result = JsonSerializer.Deserialize<LlmExtractionResult>(candidateText, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize Gemini's structured JSON output.");

            var (inputTokens, outputTokens) = ReadUsage(root);

            _logger.LogInformation(
                "Gemini extraction completed. Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens} ElapsedMs={ElapsedMs}",
                _options.Model, inputTokens, outputTokens, sw.Elapsed.TotalMilliseconds);

            return result with
            {
                Usage = new LlmUsage(inputTokens, outputTokens, EstimateCostUsd(inputTokens, outputTokens))
            };
        }
    }

    private string TruncateIfNeeded(string documentText)
    {
        if (documentText.Length <= _options.MaxInputChars) return documentText;

        _logger.LogWarning(
            "LLM input text ({Length} chars) exceeds Llm:MaxInputChars ({MaxInputChars}); truncating.",
            documentText.Length, _options.MaxInputChars);

        return documentText[.._options.MaxInputChars];
    }

    private static (int InputTokens, int OutputTokens) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
            return (0, 0);

        var inputTokens = usage.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
        var outputTokens = usage.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
        return (inputTokens, outputTokens);
    }

    private decimal EstimateCostUsd(int inputTokens, int outputTokens) =>
        inputTokens / 1_000_000m * _options.PricePerMillionInputTokensUsd +
        outputTokens / 1_000_000m * _options.PricePerMillionOutputTokensUsd;

    private static JsonObject BuildRequestBody(string documentText) => new()
    {
        ["system_instruction"] = new JsonObject
        {
            ["parts"] = new JsonArray { new JsonObject { ["text"] = LlmPromptBuilder.BuildSystemPrompt() } }
        },
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = documentText } }
            }
        },
        ["generationConfig"] = new JsonObject
        {
            ["temperature"] = 0,
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = BuildResponseSchema()
        }
    };

    /// <summary>
    /// Every field is required so Gemini always emits the key (value/sourceText may still be
    /// null inside it) — matches <see cref="LlmFieldValue"/>'s "both null = not found" contract.
    /// Field-value sub-objects are duplicated per field rather than shared via a JSON-schema
    /// $ref, since Gemini's schema subset doesn't document $ref support and a JsonNode can only
    /// have one parent anyway.
    /// </summary>
    private static JsonObject BuildResponseSchema()
    {
        string[] fieldNames =
        [
            "mauSo", "kyHieu", "soHoaDon", "ngayLap", "maCqt", "maTraCuu",
            "nguoiBanTen", "nguoiBanMst", "nguoiBanDiaChi",
            "nguoiMuaTen", "nguoiMuaMst", "nguoiMuaDiaChi",
            "tienHang", "tongTienThue", "tongThanhToan",
            "tienBangChu", "hinhThucThanhToan", "dongTien"
        ];

        var properties = new JsonObject();
        foreach (var name in fieldNames)
            properties[name] = FieldValueSchema();

        properties["chiTietThueSuat"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["thueSuat"] = FieldValueSchema(),
                    ["tienChuaThue"] = FieldValueSchema(),
                    ["tienThue"] = FieldValueSchema()
                },
                ["required"] = new JsonArray { "thueSuat", "tienChuaThue", "tienThue" }
            }
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray
            {
                "mauSo", "kyHieu", "soHoaDon", "ngayLap", "maCqt", "maTraCuu",
                "nguoiBanTen", "nguoiBanMst", "nguoiBanDiaChi",
                "nguoiMuaTen", "nguoiMuaMst", "nguoiMuaDiaChi",
                "tienHang", "tongTienThue", "tongThanhToan",
                "tienBangChu", "hinhThucThanhToan", "dongTien", "chiTietThueSuat"
            }
        };
    }

    private static JsonObject FieldValueSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["value"] = new JsonObject { ["type"] = "string", ["nullable"] = true },
            ["sourceText"] = new JsonObject { ["type"] = "string", ["nullable"] = true }
        },
        ["required"] = new JsonArray { "value", "sourceText" }
    };

    private HttpClient? BuildClient()
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("Gemini is not configured. Set Llm:Model and Llm:ApiKey to enable this provider.");
            return null;
        }

        // Timeout is enforced per-call via the linked CancellationTokenSource above (consistent
        // with AzureDocumentIntelligenceProvider/PaddleOcrProvider), not via HttpClient.Timeout.
        return new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
    }
}
