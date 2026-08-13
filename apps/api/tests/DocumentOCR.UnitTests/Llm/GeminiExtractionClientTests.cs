using System.Net;
using System.Text.Json;
using DocumentOCR.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Llm;

/// <summary>
/// Covers <see cref="GeminiExtractionClient"/> against a mocked HTTP handler — automated tests
/// must never call a real Gemini API, matching the same rule that keeps Azure/PaddleOCR out of
/// tests (see .claude/rules/testing.md).
/// </summary>
public class GeminiExtractionClientTests
{
    private static LlmOptions MakeOptions(Action<LlmOptions>? configure = null)
    {
        var options = new LlmOptions { Model = "gemini-2.5-flash", ApiKey = "test-key" };
        configure?.Invoke(options);
        return options;
    }

    private static GeminiExtractionClient CreateSut(HttpMessageHandler handler, LlmOptions? options = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
        return new GeminiExtractionClient(Options.Create(options ?? MakeOptions()), NullLogger<GeminiExtractionClient>.Instance, httpClient);
    }

    private static string WrapCandidateJson(string innerJson) =>
        $$"""
        {
          "candidates": [{
            "content": { "role": "model", "parts": [{ "text": {{JsonSerializer.Serialize(innerJson)}} }] }
          }],
          "usageMetadata": { "promptTokenCount": 1000, "candidatesTokenCount": 200 }
        }
        """;

    // ── Missing configuration ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_MissingApiKey_ThrowsWithoutCallingHttp()
    {
        var sut = new GeminiExtractionClient(
            Options.Create(MakeOptions(o => o.ApiKey = "")), NullLogger<GeminiExtractionClient>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExtractAsync("some text"));
    }

    // ── Successful mapping ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_SuccessfulResponse_MapsFieldsAndUsage()
    {
        const string innerJson = """
            {
              "soHoaDon": { "value": "00000947", "sourceText": "Số: 00000947" },
              "nguoiBanTen": { "value": "CÔNG TY TNHH ABC", "sourceText": "CÔNG TY TNHH ABC" },
              "tienHang": null,
              "chiTietThueSuat": [
                { "thueSuat": { "value": "10%", "sourceText": "10%" },
                  "tienChuaThue": { "value": "1.000.000", "sourceText": "1.000.000" },
                  "tienThue": { "value": "100.000", "sourceText": "100.000" } }
              ]
            }
            """;
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(WrapCandidateJson(innerJson)) }));

        var result = await sut.ExtractAsync("hoá đơn ...");

        Assert.Equal("00000947", result.SoHoaDon?.Value);
        Assert.Equal("Số: 00000947", result.SoHoaDon?.SourceText);
        Assert.Equal("CÔNG TY TNHH ABC", result.NguoiBanTen?.Value);
        Assert.Null(result.TienHang);
        var line = Assert.Single(result.ChiTietThueSuat);
        Assert.Equal("10%", line.ThueSuat?.Value);
        Assert.Equal("1.000.000", line.TienChuaThue?.Value);
        Assert.Equal("100.000", line.TienThue?.Value);

        Assert.Equal(1000, result.Usage.InputTokens);
        Assert.Equal(200, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task ExtractAsync_ConfiguredPricing_ComputesEstimatedCost()
    {
        var options = MakeOptions(o =>
        {
            o.PricePerMillionInputTokensUsd = 0.30m;
            o.PricePerMillionOutputTokensUsd = 2.50m;
        });
        var sut = CreateSut(
            StubHandler.Sync((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(WrapCandidateJson("{}")) }),
            options);

        var result = await sut.ExtractAsync("text");

        // 1000 input tokens * $0.30/1M + 200 output tokens * $2.50/1M
        Assert.Equal(0.0003m + 0.0005m, result.Usage.EstimatedCostUsd);
    }

    [Fact]
    public async Task ExtractAsync_NoPricingConfigured_EstimatedCostIsZero()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(WrapCandidateJson("{}")) }));

        var result = await sut.ExtractAsync("text");

        Assert.Equal(0m, result.Usage.EstimatedCostUsd);
    }

    // ── Request shape ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_Always_SendsTemperatureZeroAndJsonResponseSchema()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var sut = CreateSut(StubHandler.Sync((req, _) =>
        {
            capturedRequest = req;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(WrapCandidateJson("{}")) };
        }));

        await sut.ExtractAsync("document text here");

        Assert.NotNull(capturedRequest);
        using var bodyDoc = JsonDocument.Parse(capturedBody!);
        var generationConfig = bodyDoc.RootElement.GetProperty("generationConfig");
        Assert.Equal(0, generationConfig.GetProperty("temperature").GetInt32());
        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal(JsonValueKind.Object, generationConfig.GetProperty("responseSchema").ValueKind);

        var userText = bodyDoc.RootElement
            .GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal("document text here", userText);
    }

    [Fact]
    public async Task ExtractAsync_TextLongerThanMaxInputChars_TruncatesBeforeSending()
    {
        var longText = new string('a', 100);
        string? capturedBody = null;
        var sut = CreateSut(
            StubHandler.Sync((req, _) =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(WrapCandidateJson("{}")) };
            }),
            MakeOptions(o => o.MaxInputChars = 10));

        await sut.ExtractAsync(longText);

        using var bodyDoc = JsonDocument.Parse(capturedBody!);
        var userText = bodyDoc.RootElement
            .GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal(10, userText!.Length);
    }

    // ── Failure modes ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_HttpErrorStatus_Throws()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExtractAsync("text"));
    }

    [Fact]
    public async Task ExtractAsync_NoCandidates_Throws()
    {
        const string json = """{ "candidates": [] }""";
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        await Assert.ThrowsAnyAsync<Exception>(() => sut.ExtractAsync("text"));
    }

    [Fact]
    public async Task ExtractAsync_MalformedCandidateJson_Throws()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(WrapCandidateJson("not valid json")) }));

        await Assert.ThrowsAsync<JsonException>(() => sut.ExtractAsync("text"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handleAsync)
        : HttpMessageHandler
    {
        public static StubHandler Sync(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handle) =>
            new((req, ct) => Task.FromResult(handle(req, ct)));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            handleAsync(request, ct);
    }
}
