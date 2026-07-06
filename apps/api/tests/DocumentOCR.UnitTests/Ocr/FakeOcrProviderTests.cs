using DocumentOCR.Application.Models;
using DocumentOCR.Infrastructure.Ocr;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

public class FakeOcrProviderTests
{
    private readonly FakeOcrProvider _sut = new();

    private static DocumentInput MakeInput() => new()
    {
        Content = new MemoryStream([0x25, 0x50, 0x44, 0x46]), // %PDF magic bytes
        FileName = "test-invoice.pdf",
        ContentType = "application/pdf",
        FileSizeBytes = 4
    };

    // ── ProviderName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_Always_ReturnsFake()
    {
        Assert.Equal("Fake", _sut.ProviderName);
    }

    // ── Success flag ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ValidInput_ReturnsSuccessResult()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    // ── Page structure ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ValidInput_ReturnsOnePageResult()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.Equal(1, result.PageCount);
        Assert.Single(result.Pages);
        Assert.Equal(1, result.Pages[0].PageNumber);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_PageHasNonEmptyFullText()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.False(string.IsNullOrWhiteSpace(result.Pages[0].FullText));
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_FullTextEqualsPageFullText()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.Equal(result.Pages[0].FullText, result.FullText);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_PageHasLines()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.NotEmpty(result.Pages[0].Lines);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_LinesHaveSequentialLineNumbers()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());
        var lines = result.Pages[0].Lines;

        for (var i = 0; i < lines.Count; i++)
            Assert.Equal(i + 1, lines[i].LineNumber);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_LinesHaveBoundingBoxes()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.All(result.Pages[0].Lines, l => Assert.NotNull(l.BoundingBox));
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_WordsHaveBoundingBoxes()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());
        var words = result.Pages[0].Words;

        Assert.NotEmpty(words);
        Assert.All(words, w => Assert.NotNull(w.BoundingBox));
    }

    // ── Field candidates ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ValidInput_ReturnsSevenFieldCandidates()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.Equal(7, result.Fields.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_ContainsVendorNameField()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        var vendorName = result.Fields.SingleOrDefault(f => f.FieldKey == "VendorName");
        Assert.NotNull(vendorName);
        Assert.Equal("CÔNG TY TNHH ABC", vendorName.Value);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_ContainsInvoiceTotalField()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        var total = result.Fields.SingleOrDefault(f => f.FieldKey == "InvoiceTotal");
        Assert.NotNull(total);
        Assert.Equal("1236767", total.Value);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_AllFieldsHaveHighConfidence()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.All(result.Fields, f =>
        {
            Assert.NotNull(f.Confidence);
            Assert.True(f.Confidence >= 0.95, $"Field '{f.FieldKey}' has low confidence: {f.Confidence}");
        });
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_AllFieldsHavePageNumber()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.All(result.Fields, f =>
        {
            Assert.NotNull(f.PageNumber);
            Assert.Equal(1, f.PageNumber);
        });
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_AllFieldsHaveBoundingBoxes()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.All(result.Fields, f => Assert.NotNull(f.BoundingBox));
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_BoundingBoxHasFourPoints()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.All(result.Fields, f =>
            Assert.Equal(4, f.BoundingBox!.Points.Count));
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_FieldKeyMatchesRawProviderKey()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.All(result.Fields, f => Assert.Equal(f.FieldKey, f.RawProviderKey));
    }

    // ── Confidence ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ValidInput_DocumentConfidenceIsHigh()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.NotNull(result.Confidence);
        Assert.True(result.Confidence >= 0.95);
    }

    // ── Cost & timing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ValidInput_EstimatedCostIsZero()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.Equal(0m, result.EstimatedCost);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidInput_ProcessingTimeIsZero()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.Equal(0, result.ProcessingTimeMs);
    }

    // ── Determinism ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_CalledTwice_ReturnsSameFieldValues()
    {
        var result1 = await _sut.AnalyzeAsync(MakeInput());
        var result2 = await _sut.AnalyzeAsync(MakeInput());

        var values1 = result1.Fields.OrderBy(f => f.FieldKey).Select(f => f.Value).ToList();
        var values2 = result2.Fields.OrderBy(f => f.FieldKey).Select(f => f.Value).ToList();

        Assert.Equal(values1, values2);
    }

    // ── PageTexts backward-compat ────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_ValidInput_PageTextsMatchPagesFullText()
    {
        var result = await _sut.AnalyzeAsync(MakeInput());

        Assert.Equal(result.Pages.Select(p => p.FullText).ToList(), result.PageTexts);
    }
}
