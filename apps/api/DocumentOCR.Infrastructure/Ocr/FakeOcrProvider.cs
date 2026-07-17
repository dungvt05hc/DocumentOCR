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

    public Task<OcrResult> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
    {
        var lines = BuildLines();

        var page = new OcrPageResult
        {
            PageNumber = 1,
            FullText = string.Join(" ", lines.Select(l => l.Text)),
            Confidence = 0.98,
            Width = 8.5,
            Height = 11.0,
            Lines = lines,
            Words = lines.SelectMany(l => l.Words).ToList()
        };

        var fields = BuildFieldCandidates();

        return Task.FromResult(new OcrResult
        {
            Success = true,
            FullText = page.FullText,
            Pages = [page],
            Confidence = 0.98,
            Fields = fields,
            PageCount = 1,
            ProcessingTimeMs = 0,
            EstimatedCost = 0m,
            ModelId = "Fake",
            RawProviderResponseJson = null
        });
    }

    // ── Deterministic test data ───────────────────────────────────────────────────

    private static IReadOnlyList<OcrLineResult> BuildLines()
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
                    .Select((w, wi) => new OcrWordResult
                    {
                        Text = w,
                        Confidence = t.Item2,
                        BoundingBox = BoundingBox.FromRect(wi * 1.0, i * 0.35, 0.9, 0.3)
                    })
                    .ToList();

                return new OcrLineResult
                {
                    LineNumber = i + 1,
                    Text = t.Item1,
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
}
