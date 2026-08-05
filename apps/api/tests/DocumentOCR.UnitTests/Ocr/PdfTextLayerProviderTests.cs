using DocumentOCR.Application.Models;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

/// <summary>
/// Exercises <see cref="PdfTextLayerProvider"/> against synthetic PDFs built in-memory via
/// PdfPig's own writer (deterministic, no external fixture required) plus, when available, the
/// real MISA e-invoice sample fixture named in the OCR pipeline decision log.
/// </summary>
public class PdfTextLayerProviderTests
{
    private static PdfTextLayerProvider CreateSut(int minExtractedCharacters = 100) =>
        new(
            Options.Create(new PdfTextLayerOptions { MinExtractedCharacters = minExtractedCharacters }),
            NullLogger<PdfTextLayerProvider>.Instance);

    private static DocumentInput MakeInput(byte[] bytes, string fileName = "test.pdf") => new()
    {
        Content = new MemoryStream(bytes),
        FileName = fileName,
        ContentType = "application/pdf",
        FileSizeBytes = bytes.Length
    };

    /// <summary>Builds a minimal single/multi-page PDF with real text content, using PdfPig's own writer.</summary>
    private static byte[] BuildPdf(params string[][] pagesOfLines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var lines in pagesOfLines)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 700d;
            foreach (var line in lines)
            {
                page.AddText(line, 12, new PdfPoint(30, y), font);
                y -= 20;
            }
        }

        return builder.Build();
    }

    // ── ProviderName ─────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_Always_ReturnsPdfTextLayer()
    {
        Assert.Equal("PdfTextLayer", CreateSut().ProviderName);
    }

    // ── Successful text-layer read ──────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_PdfWithTextLayer_ReturnsSuccessWithLinesAndWords()
    {
        var pdfBytes = BuildPdf([
            [
                "INVOICE NUMBER 00000947",
                "TAX CODE 4500401790",
                "SUBTOTAL 10019909",
                "VAT 1001991",
                "TOTAL 11021900"
            ]
        ]);

        var result = await CreateSut(minExtractedCharacters: 10).AnalyzeAsync(MakeInput(pdfBytes));

        Assert.True(result.Success);
        Assert.Equal("PdfTextLayer", result.ProviderName);
        Assert.Equal("PdfTextLayer", result.ModelId);
        Assert.Equal(0m, result.EstimatedCost);
        Assert.Equal(1, result.PageCount);
        Assert.Single(result.Pages);

        var page = result.Pages[0];
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(5, page.Lines.Count);
        Assert.Contains("00000947", page.FullText);
        Assert.Contains("4500401790", page.FullText);

        // Every word carries the fixed 0.95 confidence and a populated bounding box.
        Assert.NotEmpty(page.Words);
        Assert.All(page.Words, w =>
        {
            Assert.Equal(0.95, w.Confidence);
            Assert.NotNull(w.BoundingBox);
            Assert.Equal(1, w.PageNumber);
        });

        Assert.Equal(0.95, result.AverageConfidence!.Value, precision: 10);
    }

    [Fact]
    public async Task AnalyzeAsync_LinesWithinAPage_AreOrderedTopToBottom()
    {
        var pdfBytes = BuildPdf([["FIRST LINE", "SECOND LINE", "THIRD LINE"]]);

        var result = await CreateSut(minExtractedCharacters: 5).AnalyzeAsync(MakeInput(pdfBytes));

        var lineTexts = result.Pages[0].Lines.OrderBy(l => l.LineNumber).Select(l => l.Text).ToList();
        Assert.Equal(["FIRST LINE", "SECOND LINE", "THIRD LINE"], lineTexts);
    }

    [Fact]
    public async Task AnalyzeAsync_WordsWithinALine_AreOrderedLeftToRight()
    {
        var pdfBytes = BuildPdf([["ALPHA BETA GAMMA"]]);

        var result = await CreateSut(minExtractedCharacters: 5).AnalyzeAsync(MakeInput(pdfBytes));

        var line = Assert.Single(result.Pages[0].Lines);
        Assert.Equal(["ALPHA", "BETA", "GAMMA"], line.Words.Select(w => w.Text).ToList());
    }

    [Fact]
    public async Task AnalyzeAsync_MultiPagePdf_CountsPagesCorrectly()
    {
        var pdfBytes = BuildPdf([["PAGE ONE CONTENT HERE"], ["PAGE TWO CONTENT HERE"]]);

        var result = await CreateSut(minExtractedCharacters: 5).AnalyzeAsync(MakeInput(pdfBytes));

        Assert.True(result.Success);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal([1, 2], result.Pages.Select(p => p.PageNumber).ToList());
    }

    // ── Scan detection ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_TextBelowMinCharacterThreshold_ReturnsFailureLikelyScanned()
    {
        var pdfBytes = BuildPdf([["hi"]]); // 2 characters, well under any realistic threshold

        var result = await CreateSut(minExtractedCharacters: 100).AnalyzeAsync(MakeInput(pdfBytes));

        Assert.False(result.Success);
        Assert.Equal("PdfTextLayer", result.ProviderName);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("scanned", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_BlankPdfWithNoText_ReturnsFailureNotThrow()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4); // page with no text at all
        var pdfBytes = builder.Build();

        var result = await CreateSut().AnalyzeAsync(MakeInput(pdfBytes));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── Corrupt input ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_CorruptPdfBytes_ReturnsFailureNotThrow()
    {
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };

        var result = await CreateSut().AnalyzeAsync(MakeInput(garbage));

        Assert.False(result.Success);
        Assert.Equal("PdfTextLayer", result.ProviderName);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyStream_ReturnsFailureNotThrow()
    {
        var result = await CreateSut().AnalyzeAsync(MakeInput([]));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── Real fixture (provided separately) ─────────────────────────────────────

    /// <summary>
    /// Exercises the real MISA e-invoice sample referenced in the PDF-text-layer feature request.
    /// The fixture is not committed to the repo (real invoice content) and must be dropped in
    /// manually at <c>apps/api/tests/Fixtures/misa-einvoice-sample.pdf</c> -- this test is a no-op
    /// (trivially passes without asserting anything) until that file exists, since xUnit v2 has no
    /// built-in runtime-conditional skip.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_MisaEInvoiceSample_ExtractsExpectedFieldsWhenFixturePresent()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "misa-einvoice-sample.pdf");
        if (!File.Exists(fixturePath))
            return;

        await using var stream = File.OpenRead(fixturePath);
        var input = new DocumentInput
        {
            Content = stream,
            FileName = "misa-einvoice-sample.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = stream.Length
        };

        var result = await CreateSut().AnalyzeAsync(input);

        Assert.True(result.Success);
        Assert.Contains("4500401790", result.FullText);
        Assert.Contains("1C26TNH", result.FullText);
        Assert.Contains("00000947", result.FullText);
        Assert.Contains("31/07/2026", result.FullText);
        Assert.Contains("10.019.909", result.FullText);
        Assert.Contains("1.001.991", result.FullText);
        Assert.Contains("11.021.900", result.FullText);
    }
}
