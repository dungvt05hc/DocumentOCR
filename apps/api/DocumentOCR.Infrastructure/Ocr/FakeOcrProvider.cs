using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// Deterministic fake OCR provider for local development and unit testing.
/// Returns a pre-set Vietnamese invoice result; no network calls are made.
/// </summary>
public sealed class FakeOcrProvider : IDocumentOcrProvider
{
    public string ProviderName => "Fake";

    public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
    {
        var lines = BuildLines();

        var page = new OcrPage
        {
            PageNumber = 1,
            FullText = string.Join(" ", lines.Select(l => l.Text)),
            Confidence = 0.98,
            Width = 8.5,
            Height = 11.0,
            Unit = "inch",
            Lines = lines,
            Words = lines.SelectMany(l => l.Words).ToList()
        };

        var fields = BuildFieldCandidates();

        return Task.FromResult(new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = ProviderName,
            ModelId = "Fake",
            FullText = page.FullText,
            Pages = [page],
            AverageConfidence = 0.98,
            Fields = fields,
            Paragraphs = BuildParagraphs(),
            KeyValuePairs = BuildKeyValuePairs(),
            PageCount = 1,
            ProcessingTimeMs = 0,
            EstimatedCost = 0m,
            RawProviderResponseJson = null
        });
    }

    // ── Deterministic test data ───────────────────────────────────────────────────

    private static IReadOnlyList<OcrLine> BuildLines()
    {
        var rawLines = new[]
        {
            ("HOÁ ĐƠN GIÁ TRỊ GIA TĂNG",     0.99),
            ("Số: 0001234",                    0.99),
            ("Ngày: 31/12/2024",               0.99),
            ("Đơn vị bán hàng: CÔNG TY TNHH ABC", 0.98),
            ("Mã số thuế: 0100109106",          0.99),
            ("Thành tiền chưa thuế: 1.030.639", 0.97),
            ("Thuế GTGT (20%): 206.128",        0.97),
            ("Tổng cộng tiền thanh toán: 1.236.767", 0.98)
        };

        return rawLines
            .Select((t, i) =>
            {
                var words = t.Item1.Split(' ')
                    .Select((w, wi) => new OcrWord
                    {
                        Text = w,
                        PageNumber = 1,
                        Confidence = t.Item2,
                        BoundingBox = BoundingBox.FromRect(wi * 1.0, i * 0.35, 0.9, 0.3)
                    })
                    .ToList();

                return new OcrLine
                {
                    LineNumber = i + 1,
                    Text = t.Item1,
                    PageNumber = 1,
                    Confidence = t.Item2,
                    BoundingBox = BoundingBox.FromRect(0, i * 0.35, 8.0, 0.3),
                    Words = words
                };
            })
            .ToList();
    }

    private static IReadOnlyList<OcrFieldCandidate> BuildFieldCandidates() =>
    [
        new() { FieldKey = "VendorName",   Value = "CÔNG TY TNHH ABC",  Confidence = 0.99, PageNumber = 1,
                RawProviderKey = "VendorName",   BoundingBox = BoundingBox.FromRect(0.5, 1.05, 3.5, 0.3) },
        new() { FieldKey = "VendorTaxId",  Value = "0100109106",        Confidence = 0.99, PageNumber = 1,
                RawProviderKey = "VendorTaxId",  BoundingBox = BoundingBox.FromRect(0.5, 1.40, 2.0, 0.3) },
        new() { FieldKey = "InvoiceId",    Value = "0001234",           Confidence = 0.99, PageNumber = 1,
                RawProviderKey = "InvoiceId",    BoundingBox = BoundingBox.FromRect(0.5, 0.35, 1.5, 0.3) },
        new() { FieldKey = "InvoiceDate",  Value = "2024-12-31",        Confidence = 0.99, PageNumber = 1,
                RawProviderKey = "InvoiceDate",  BoundingBox = BoundingBox.FromRect(0.5, 0.70, 2.0, 0.3) },
        new() { FieldKey = "SubTotal",     Value = "1030639",           Confidence = 0.97, PageNumber = 1,
                RawProviderKey = "SubTotal",     BoundingBox = BoundingBox.FromRect(5.0, 1.75, 2.5, 0.3) },
        new() { FieldKey = "TotalTax",     Value = "206128",            Confidence = 0.97, PageNumber = 1,
                RawProviderKey = "TotalTax",     BoundingBox = BoundingBox.FromRect(5.0, 2.10, 2.5, 0.3) },
        new() { FieldKey = "InvoiceTotal", Value = "1236767",           Confidence = 0.98, PageNumber = 1,
                RawProviderKey = "InvoiceTotal", BoundingBox = BoundingBox.FromRect(5.0, 2.45, 2.5, 0.3) }
    ];

    private static IReadOnlyList<OcrParagraph> BuildParagraphs() =>
    [
        new() { Text = "HOÁ ĐƠN GIÁ TRỊ GIA TĂNG", Role = "title", PageNumber = 1 }
    ];

    /// <summary>
    /// Mirrors the structured field candidates as layout key-value pairs, so the
    /// KeyValuePair-driven extraction path (used when only prebuilt-layout is available)
    /// can be exercised deterministically against this provider too.
    /// </summary>
    private static IReadOnlyList<OcrKeyValuePair> BuildKeyValuePairs() =>
    [
        new() { KeyText = "Đơn vị bán hàng", ValueText = "CÔNG TY TNHH ABC", Confidence = 0.97, PageNumber = 1 },
        new() { KeyText = "Mã số thuế", ValueText = "0100109106", Confidence = 0.97, PageNumber = 1 },
        new() { KeyText = "Số", ValueText = "0001234", Confidence = 0.97, PageNumber = 1 },
        new() { KeyText = "Ngày", ValueText = "31/12/2024", Confidence = 0.97, PageNumber = 1 },
        new() { KeyText = "Tổng cộng tiền thanh toán", ValueText = "1.236.767", Confidence = 0.97, PageNumber = 1 }
    ];
}
