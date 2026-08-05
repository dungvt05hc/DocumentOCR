using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

/// <summary>
/// Exercises <see cref="PdfProviderRouter"/>'s branch selection using hand-written recording
/// stubs for both the text-layer and OCR-fallback providers (no real PDF parsing needed to prove
/// which branch was taken and which was skipped) -- same style as
/// <c>DocumentProcessingServiceTests.StubOcrProvider</c>/<c>RecordingOcrProvider</c>.
/// </summary>
public class PdfProviderRouterTests
{
    private static DocumentInput MakeInput(string contentType, string fileName = "document") => new()
    {
        Content = new MemoryStream([1, 2, 3, 4]),
        FileName = fileName,
        ContentType = contentType,
        FileSizeBytes = 4
    };

    // ── PDF: text layer succeeds ────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_PdfWithUsableTextLayer_ReturnsTextLayerResultAndNeverCallsOcrProvider()
    {
        var textLayer = new RecordingOcrProvider(new NormalizedOcrDocument
        {
            Success = true, ProviderName = "PdfTextLayer", ModelId = "PdfTextLayer", PageCount = 1
        });
        var ocrFallback = new RecordingOcrProvider(new NormalizedOcrDocument
        {
            Success = true, ProviderName = "Fake", PageCount = 1
        });
        var sut = new PdfProviderRouter(textLayer, ocrFallback, NullLogger<PdfProviderRouter>.Instance);

        var result = await sut.AnalyzeAsync(MakeInput("application/pdf"));

        Assert.Equal(1, textLayer.CallCount);
        Assert.Equal(0, ocrFallback.CallCount);
        Assert.Equal("PdfTextLayer", result.ProviderName);
    }

    // ── PDF: text layer reports scan/failure -> fallback ────────────────────────

    [Fact]
    public async Task AnalyzeAsync_PdfDetectedAsScan_FallsBackToOcrProvider()
    {
        var textLayer = new RecordingOcrProvider(new NormalizedOcrDocument
        {
            Success = false, ProviderName = "PdfTextLayer", ErrorMessage = "likely a scanned PDF"
        });
        var ocrFallback = new RecordingOcrProvider(new NormalizedOcrDocument
        {
            Success = true, ProviderName = "AzureDocumentIntelligence", ModelId = "prebuilt-layout", PageCount = 1
        });
        var sut = new PdfProviderRouter(textLayer, ocrFallback, NullLogger<PdfProviderRouter>.Instance);

        var result = await sut.AnalyzeAsync(MakeInput("application/pdf"));

        Assert.Equal(1, textLayer.CallCount);
        Assert.Equal(1, ocrFallback.CallCount);
        Assert.Equal("AzureDocumentIntelligence", result.ProviderName);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task AnalyzeAsync_PdfTextLayerReadFails_FallsBackToOcrProvider()
    {
        var textLayer = new RecordingOcrProvider(new NormalizedOcrDocument
        {
            Success = false, ProviderName = "PdfTextLayer", ErrorMessage = "Failed to read PDF text layer: corrupt"
        });
        var ocrFallback = new RecordingOcrProvider(new NormalizedOcrDocument
        {
            Success = true, ProviderName = "AzureDocumentIntelligence"
        });
        var sut = new PdfProviderRouter(textLayer, ocrFallback, NullLogger<PdfProviderRouter>.Instance);

        var result = await sut.AnalyzeAsync(MakeInput("application/pdf"));

        Assert.Equal(1, ocrFallback.CallCount);
        Assert.True(result.Success);
    }

    // ── Non-PDF content types: straight to OCR, text-layer branch never touched ─

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task AnalyzeAsync_ImageContentType_NeverCallsTextLayerProvider(string contentType)
    {
        var textLayer = new RecordingOcrProvider(new NormalizedOcrDocument { Success = true, ProviderName = "PdfTextLayer" });
        var ocrFallback = new RecordingOcrProvider(new NormalizedOcrDocument { Success = true, ProviderName = "Fake", PageCount = 1 });
        var sut = new PdfProviderRouter(textLayer, ocrFallback, NullLogger<PdfProviderRouter>.Instance);

        var result = await sut.AnalyzeAsync(MakeInput(contentType));

        Assert.Equal(0, textLayer.CallCount);
        Assert.Equal(1, ocrFallback.CallCount);
        Assert.Equal("Fake", result.ProviderName);
    }

    private sealed class RecordingOcrProvider(NormalizedOcrDocument result) : IDocumentOcrProvider
    {
        public int CallCount { get; private set; }

        public string ProviderName => result.ProviderName;

        public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
