using System.Diagnostics;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// <see cref="IDocumentOcrProvider"/> that reads a PDF's embedded text layer directly via PdfPig
/// instead of running OCR. Only correct for software-generated PDFs (e-invoices from MISA,
/// Viettel, VNPT, BKAV, ...) that already carry accurate text — never for scanned PDFs, which
/// have no text layer to read. This provider does not attempt OCR itself; when the text layer is
/// too small to be usable it reports failure (see <see cref="AnalyzeAsync"/>) and leaves falling
/// back to a real OCR provider to <see cref="PdfProviderRouter"/>.
/// <para>
/// Registered as a <b>Singleton</b> -- PdfPig documents are opened per-call from the input stream,
/// so this class holds no per-request state.
/// </para>
/// </summary>
public sealed class PdfTextLayerProvider : IDocumentOcrProvider
{
    private readonly PdfTextLayerOptions _options;
    private readonly ILogger<PdfTextLayerProvider> _logger;

    public string ProviderName => "PdfTextLayer";

    public PdfTextLayerProvider(IOptions<PdfTextLayerOptions> options, ILogger<PdfTextLayerProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var ms = new MemoryStream();
            input.Content.CopyTo(ms);
            ms.Position = 0;

            using var document = PdfDocument.Open(ms);

            var pages = document.GetPages().Select(BuildPageResult).ToList();
            sw.Stop();

            var totalExtractedChars = pages.Sum(p => p.FullText.Trim().Length);
            if (totalExtractedChars < _options.MinExtractedCharacters)
            {
                var scanMsg =
                    $"Extracted only {totalExtractedChars} character(s) of text layer across " +
                    $"{pages.Count} page(s) (minimum {_options.MinExtractedCharacters}) -- likely a " +
                    $"scanned PDF with no embedded text layer for '{input.FileName}'.";
                _logger.LogInformation(scanMsg);
                return Task.FromResult(Failure(scanMsg, sw.Elapsed.TotalMilliseconds, pages.Count));
            }

            var fullText = string.Join("\n", pages.Select(p => p.FullText));
            var averageConfidence = CalculateAverageConfidence(pages);

            _logger.LogInformation(
                "PDF text layer read successfully. File={FileName} Pages={PageCount} Characters={CharCount} Elapsed={ElapsedMs}ms",
                input.FileName, pages.Count, totalExtractedChars, sw.Elapsed.TotalMilliseconds);

            return Task.FromResult(new NormalizedOcrDocument
            {
                Success = true,
                ProviderName = ProviderName,
                ModelId = ProviderName,
                FullText = fullText,
                Pages = pages,
                AverageConfidence = averageConfidence,
                PageCount = pages.Count,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
                EstimatedCost = 0m
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            var msg = $"Failed to read PDF text layer for '{input.FileName}': {ex.Message}";
            _logger.LogWarning(ex, msg);
            return Task.FromResult(Failure(msg, sw.Elapsed.TotalMilliseconds, 0));
        }
    }

    // ── Page/line/word mapping ──────────────────────────────────────────────────

    private static OcrPage BuildPageResult(Page page)
    {
        var lines = BuildLines(page);

        return new OcrPage
        {
            PageNumber = page.Number,
            FullText = page.Text,
            Width = page.Width,
            Height = page.Height,
            Unit = "point",
            Lines = lines,
            Words = lines.SelectMany(l => l.Words).ToList()
        };
    }

    /// <summary>
    /// Groups the page's words into lines by Y-coordinate proximity (tolerance derived from word
    /// height), then sorts each line's words left-to-right by X. Words come back from PdfPig
    /// already roughly ordered top-to-bottom, so a single pass comparing each word against the
    /// running line it would join is enough -- no need for a full clustering pass.
    /// </summary>
    private static List<OcrLine> BuildLines(Page page)
    {
        var words = page.GetWords()?.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList() ?? [];
        if (words.Count == 0) return [];

        // PdfPig's Y axis increases upward (origin bottom-left) -- descending order visits the
        // top of the page first, matching natural reading order.
        var ordered = words.OrderByDescending(w => CenterY(w.BoundingBox)).ToList();

        var groups = new List<List<Word>>();
        List<Word>? current = null;
        double currentCenterYTotal = 0;
        double currentTolerance = 0;

        foreach (var word in ordered)
        {
            var centerY = CenterY(word.BoundingBox);

            if (current is not null &&
                Math.Abs(centerY - currentCenterYTotal / current.Count) <= currentTolerance)
            {
                current.Add(word);
                currentCenterYTotal += centerY;
            }
            else
            {
                current = [word];
                groups.Add(current);
                currentCenterYTotal = centerY;
                currentTolerance = Math.Max(word.BoundingBox.Height * 0.6, 1.0);
            }
        }

        return groups
            .Select((group, i) => BuildLine(group, i + 1, page.Number, page.Height))
            .ToList();
    }

    private static OcrLine BuildLine(List<Word> group, int lineNumber, int pageNumber, double pageHeight)
    {
        var ordered = group.OrderBy(w => w.BoundingBox.Left).ToList();
        var words = ordered.Select(w => BuildWordResult(w, pageNumber, pageHeight)).ToList();

        var left = ordered.Min(w => w.BoundingBox.Left);
        var right = ordered.Max(w => w.BoundingBox.Right);
        var top = ordered.Max(w => w.BoundingBox.Top);
        var bottom = ordered.Min(w => w.BoundingBox.Bottom);

        return new OcrLine
        {
            LineNumber = lineNumber,
            Text = string.Join(" ", ordered.Select(w => w.Text)),
            PageNumber = pageNumber,
            BoundingBox = ToBoundingBox(left, top, right, bottom, pageHeight),
            Words = words
        };
    }

    private static OcrWord BuildWordResult(Word word, int pageNumber, double pageHeight) => new()
    {
        Text = word.Text,
        PageNumber = pageNumber,
        Confidence = 0.95,
        BoundingBox = ToBoundingBox(
            word.BoundingBox.Left, word.BoundingBox.Top, word.BoundingBox.Right, word.BoundingBox.Bottom, pageHeight)
    };

    /// <summary>
    /// Converts a PdfPig rectangle (origin bottom-left, Y increasing upward) into this pipeline's
    /// bounding-box convention (origin top-left, Y increasing downward -- same as
    /// <see cref="BoundingBox.FromRect"/> and the Azure provider's polygon mapping).
    /// </summary>
    private static BoundingBox ToBoundingBox(double left, double top, double right, double bottom, double pageHeight) =>
        new([
            new BoundingPoint(left, pageHeight - top),
            new BoundingPoint(right, pageHeight - top),
            new BoundingPoint(right, pageHeight - bottom),
            new BoundingPoint(left, pageHeight - bottom)
        ]);

    private static double CenterY(PdfRectangle box) => (box.Top + box.Bottom) / 2;

    private static double? CalculateAverageConfidence(IReadOnlyList<OcrPage> pages)
    {
        var confidences = pages
            .SelectMany(p => p.Words)
            .Where(w => w.Confidence.HasValue)
            .Select(w => w.Confidence!.Value)
            .ToList();

        return confidences.Count > 0 ? confidences.Average() : null;
    }

    private NormalizedOcrDocument Failure(string message, double processingTimeMs, int pageCount) => new()
    {
        Success = false,
        ProviderName = ProviderName,
        ModelId = ProviderName,
        ErrorMessage = message,
        ProcessingTimeMs = processingTimeMs,
        PageCount = pageCount
    };
}
