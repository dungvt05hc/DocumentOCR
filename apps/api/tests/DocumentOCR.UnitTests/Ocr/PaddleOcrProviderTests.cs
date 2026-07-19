using System.Net;
using DocumentOCR.Application.Models;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

/// <summary>
/// Covers <see cref="PaddleOcrProvider"/> against a mocked HTTP handler — automated tests must
/// never call a real PaddleOCR service, matching the same rule that keeps Azure out of tests
/// (see .claude/rules/testing.md).
/// </summary>
public class PaddleOcrProviderTests
{
    private const string BaseUrl = "http://localhost:8866";

    private static DocumentInput MakeInput() => new()
    {
        Content = new MemoryStream([0x25, 0x50, 0x44, 0x46]), // %PDF magic bytes
        FileName = "test-invoice.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 4
    };

    private static PaddleOcrProvider CreateSut(HttpMessageHandler handler, PaddleOcrOptions? options = null)
    {
        options ??= new PaddleOcrOptions { BaseUrl = BaseUrl };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        return new PaddleOcrProvider(Options.Create(options), NullLogger<PaddleOcrProvider>.Instance, httpClient);
    }

    // ── ProviderName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_Always_ReturnsPaddle()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Equal("Paddle", sut.ProviderName);
    }

    // ── Missing configuration ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MissingBaseUrl_ReturnsFailureWithoutThrowing()
    {
        var sut = new PaddleOcrProvider(
            Options.Create(new PaddleOcrOptions { BaseUrl = "" }),
            NullLogger<PaddleOcrProvider>.Instance);

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    // ── Successful mapping ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_SuccessfulResponse_MapsToNormalizedOcrDocument()
    {
        const string json = """
            {
              "success": true,
              "pageCount": 1,
              "fullText": "MOTA CAFE Tổng: 85.000",
              "averageConfidence": 0.93,
              "pages": [{
                "pageNumber": 1, "width": 800, "height": 1200, "unit": "pixel",
                "lines": [
                  {
                    "text": "MOTA CAFE", "confidence": 0.95,
                    "boundingBox": [[10,20],[200,20],[200,50],[10,50]],
                    "words": [
                      { "text": "MOTA", "confidence": 0.96, "boundingBox": [[10,20],[80,20],[80,50],[10,50]] },
                      { "text": "CAFE", "confidence": 0.94, "boundingBox": [[90,20],[200,20],[200,50],[90,50]] }
                    ]
                  },
                  { "text": "Tổng: 85.000", "confidence": 0.9, "boundingBox": [[10,60],[200,60],[200,90],[10,90]] }
                ]
              }]
            }
            """;

        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.True(result.Success);
        Assert.Equal("Paddle", result.ProviderName);
        Assert.Equal(1, result.PageCount);
        Assert.Equal("MOTA CAFE Tổng: 85.000", result.FullText);
        Assert.Equal(0.93, result.AverageConfidence);
        Assert.Single(result.Pages);

        var page = result.Pages[0];
        Assert.Equal(2, page.Lines.Count);
        Assert.Equal("MOTA CAFE", page.Lines[0].Text);
        Assert.NotNull(page.Lines[0].BoundingBox);
        Assert.Equal(4, page.Lines[0].BoundingBox!.Points.Count);
        Assert.Equal(2, page.Lines[0].Words.Count);
        Assert.Equal("MOTA", page.Lines[0].Words[0].Text);
        Assert.Equal(0.96, page.Lines[0].Words[0].Confidence);
    }

    [Fact]
    public async Task AnalyzeAsync_SuccessfulResponseWithoutWords_MapsLinesWithEmptyWordsList()
    {
        const string json = """
            {
              "success": true,
              "pageCount": 1,
              "pages": [{
                "pageNumber": 1,
                "lines": [{ "text": "Some text", "confidence": 0.9 }]
              }]
            }
            """;

        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.True(result.Success);
        Assert.Empty(result.Pages[0].Lines[0].Words);
    }

    // ── PaddleOCR-reported failure ────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ServiceReportsFailureInBody_ReturnsFailureWithMessage()
    {
        const string json = """{ "success": false, "errorMessage": "Unsupported file format" }""";

        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Equal("Unsupported file format", result.ErrorMessage);
    }

    // ── Service unavailable ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ServiceUnreachable_ReturnsFailureWithoutThrowing()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            throw new HttpRequestException("Connection refused")));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Contains("unreachable", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_NonSuccessStatusCode_ReturnsFailureWithStatusInMessage()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }

    // ── Timeout ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_TimesOut_ReturnsFailureWithoutThrowing()
    {
        var sut = CreateSut(
            new StubHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            new PaddleOcrOptions { BaseUrl = BaseUrl, TimeoutSeconds = 1 });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    // ── Invalid JSON ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_InvalidJsonResponse_ReturnsFailureWithoutThrowing()
    {
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json at all") }));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Contains("could not be parsed", result.ErrorMessage);
    }

    // ── Cost ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_SuccessfulResponse_EstimatedCostIsZero()
    {
        const string json = """{ "success": true, "pageCount": 1 }""";
        var sut = CreateSut(StubHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.Equal(0m, result.EstimatedCost);
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
