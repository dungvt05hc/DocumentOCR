using Azure.AI.DocumentIntelligence;
using DocumentOCR.Application.Models;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

/// <summary>
/// Covers only the code paths that do not require a real Azure endpoint (missing
/// credentials, invalid endpoint URI). Automated tests must never call Azure —
/// see .claude/rules/testing.md.
/// </summary>
public class AzureDocumentIntelligenceProviderTests
{
    private static DocumentInput MakeInput() => new()
    {
        Content = new MemoryStream([0x25, 0x50, 0x44, 0x46]), // %PDF magic bytes
        FileName = "test-invoice.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 4
    };

    private static AzureDocumentIntelligenceProvider CreateSut(AzureOcrOptions options) =>
        new(Options.Create(options), NullLogger<AzureDocumentIntelligenceProvider>.Instance);

    // ── ProviderName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_Always_ReturnsAzureDocumentIntelligence()
    {
        var sut = CreateSut(new AzureOcrOptions());

        Assert.Equal("AzureDocumentIntelligence", sut.ProviderName);
    }

    // ── Missing credentials ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MissingEndpointAndApiKey_ReturnsFailureWithoutThrowing()
    {
        var sut = CreateSut(new AzureOcrOptions { Endpoint = "", ApiKey = "" });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_MissingApiKeyOnly_ReturnsFailure()
    {
        var sut = CreateSut(new AzureOcrOptions
        {
            Endpoint = "https://example.cognitiveservices.azure.com/",
            ApiKey = ""
        });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_NotConfigured_SetsModelIdFromOptions()
    {
        var sut = CreateSut(new AzureOcrOptions
        {
            Endpoint = "",
            ApiKey = "",
            DefaultModelId = "prebuilt-receipt"
        });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.Equal("prebuilt-receipt", result.ModelId);
    }

    [Fact]
    public async Task AnalyzeAsync_NotConfigured_ReturnsZeroProcessingTime()
    {
        var sut = CreateSut(new AzureOcrOptions { Endpoint = "", ApiKey = "" });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.Equal(0, result.ProcessingTimeMs);
    }

    [Fact]
    public async Task AnalyzeAsync_NotConfigured_ReturnsEmptyPages()
    {
        var sut = CreateSut(new AzureOcrOptions { Endpoint = "", ApiKey = "" });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.Empty(result.Pages);
    }

    // ── Invalid endpoint ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_InvalidEndpointUri_ReturnsFailureWithoutThrowing()
    {
        var sut = CreateSut(new AzureOcrOptions
        {
            Endpoint = "not-a-valid-uri",
            ApiKey = "some-key"
        });

        var result = await sut.AnalyzeAsync(MakeInput());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── AzureOcrOptions.IsConfigured ─────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("https://example.cognitiveservices.azure.com/", "")]
    [InlineData("", "some-key")]
    public void IsConfigured_MissingEndpointOrApiKey_ReturnsFalse(string endpoint, string apiKey)
    {
        var options = new AzureOcrOptions { Endpoint = endpoint, ApiKey = apiKey };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_EndpointAndApiKeyPresent_ReturnsTrue()
    {
        var options = new AzureOcrOptions
        {
            Endpoint = "https://example.cognitiveservices.azure.com/",
            ApiKey = "some-key"
        };

        Assert.True(options.IsConfigured);
    }

    // ── AzureOcrOptions defaults ─────────────────────────────────────────────────

    [Fact]
    public void Defaults_DefaultModelId_IsPrebuiltLayout()
    {
        Assert.Equal("prebuilt-layout", new AzureOcrOptions().DefaultModelId);
    }

    [Fact]
    public void Defaults_FeaturesAndBenchmarkModelIds_AreEmpty()
    {
        var options = new AzureOcrOptions();

        Assert.Empty(options.Features);
        Assert.Empty(options.BenchmarkModelIds);
    }

    // ── ApplyFeatures (pure — no Azure call; AnalyzeDocumentOptions is just a request DTO) ───

    [Fact]
    public void ApplyFeatures_KnownFeatureName_IsAddedToRequest()
    {
        var analyzeOptions = new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes([1]));

        AzureDocumentIntelligenceProvider.ApplyFeatures(
            analyzeOptions, ["keyValuePairs"], "test.pdf", NullLogger.Instance);

        Assert.Contains(DocumentAnalysisFeature.KeyValuePairs, analyzeOptions.Features);
    }

    [Fact]
    public void ApplyFeatures_UnknownFeatureName_IsSkippedWithoutThrowing()
    {
        var analyzeOptions = new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes([1]));

        AzureDocumentIntelligenceProvider.ApplyFeatures(
            analyzeOptions, ["notARealFeature"], "test.pdf", NullLogger.Instance);

        Assert.Empty(analyzeOptions.Features);
    }

    [Fact]
    public void ApplyFeatures_EmptyList_LeavesRequestFeaturesEmpty()
    {
        var analyzeOptions = new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes([1]));

        AzureDocumentIntelligenceProvider.ApplyFeatures(analyzeOptions, [], "test.pdf", NullLogger.Instance);

        Assert.Empty(analyzeOptions.Features);
    }

    [Fact]
    public void ApplyFeatures_IsCaseInsensitive()
    {
        var analyzeOptions = new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes([1]));

        AzureDocumentIntelligenceProvider.ApplyFeatures(
            analyzeOptions, ["KEYVALUEPAIRS"], "test.pdf", NullLogger.Instance);

        Assert.Contains(DocumentAnalysisFeature.KeyValuePairs, analyzeOptions.Features);
    }
}
