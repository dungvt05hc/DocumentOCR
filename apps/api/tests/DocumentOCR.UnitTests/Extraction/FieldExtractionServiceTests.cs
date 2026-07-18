using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Extraction;

public class FieldExtractionServiceTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private readonly FieldExtractionService _sut = new();

    [Fact]
    public void Extract_VietnameseInvoiceLines_ReturnsExpectedFields()
    {
        var ocr = OcrFromLines(
            "HÓA ĐƠN GIÁ TRỊ GIA TĂNG",
            "Số hóa đơn: AA/24E-0001234",
            "Ngày hóa đơn: 31/12/2024",
            "Đơn vị bán hàng: CÔNG TY TNHH ABC",
            "Mã số thuế: 0100109106-001",
            "Cộng tiền hàng: 1.000.000",
            "Thuế GTGT (10%): 100.000",
            "Tổng thanh toán: ₫1.100.000",
            "Ghi chú: Khách thanh toán chuyển khoản");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierName, "CÔNG TY TNHH ABC");
        AssertField(fields, FieldName.SupplierTaxCode, "0100109106-001");
        AssertField(fields, FieldName.InvoiceNumber, "AA/24E-0001234");
        AssertField(fields, FieldName.InvoiceDate, "31/12/2024");
        AssertField(fields, FieldName.SubtotalAmount, "1.000.000");
        AssertField(fields, FieldName.VatAmount, "100.000");
        AssertField(fields, FieldName.TotalAmount, "₫1.100.000");
        AssertField(fields, FieldName.Currency, "₫");
        AssertField(fields, FieldName.DocumentType, nameof(DocumentType.Invoice));
        AssertField(fields, FieldName.Notes, "Khách thanh toán chuyển khoản");
    }

    [Fact]
    public void Extract_ReceiptLines_ReturnsReceiptDocumentTypeAndTotals()
    {
        var ocr = OcrFromLines(
            "PHIẾU THU",
            "Nhà cung cấp: HỘ KINH DOANH MINH AN",
            "MST: 0312345678",
            "Invoice No: RC-9988",
            "Invoice date: 2024-05-01",
            "Subtotal: 1,234,567 VND",
            "VAT: 123,457",
            "Tổng cộng: 1,358,024 VNĐ");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.DocumentType, nameof(DocumentType.Receipt));
        AssertField(fields, FieldName.SupplierName, "HỘ KINH DOANH MINH AN");
        AssertField(fields, FieldName.SupplierTaxCode, "0312345678");
        AssertField(fields, FieldName.InvoiceNumber, "RC-9988");
        AssertField(fields, FieldName.TotalAmount, "1,358,024 VNĐ");
        AssertField(fields, FieldName.Currency, "VND");
    }

    [Fact]
    public void Extract_FullTextWithoutLines_UsesRegexFallback()
    {
        var ocr = new OcrResult
        {
            Success = true,
            FullText = """
                HÓA ĐƠN BÁN HÀNG
                MST: 0100109106
                Số HĐ: 0005678
                Ngày: 1/7/2026
                Cộng tiền hàng: 2 000 000
                Thuế GTGT: 200 000
                Tổng tiền: 2 200 000 VND
                """,
            Confidence = 0.8,
            PageCount = 1
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierTaxCode, "0100109106");
        AssertField(fields, FieldName.InvoiceNumber, "0005678");
        AssertField(fields, FieldName.InvoiceDate, "1/7/2026");
        AssertField(fields, FieldName.SubtotalAmount, "2 000 000");
        AssertField(fields, FieldName.VatAmount, "200 000");
        AssertField(fields, FieldName.TotalAmount, "2 200 000 VND");
        // "HÓA ĐƠN BÁN HÀNG" is a sales receipt, not a VAT invoice, despite the "hóa đơn" substring.
        AssertField(fields, FieldName.DocumentType, nameof(DocumentType.Receipt));
    }

    [Fact]
    public void Extract_StructuredFieldHasHigherConfidence_KeepsStructuredValue()
    {
        var ocr = OcrFromLines(
            "Số hóa đơn: OCR-LOW",
            "Tổng thanh toán: 9.999");

        ocr = new OcrResult
        {
            Success = true,
            FullText = ocr.FullText,
            Pages = ocr.Pages,
            Confidence = ocr.Confidence,
            PageCount = 1,
            Fields =
            [
                new()
                {
                    FieldKey = "InvoiceId",
                    Value = "STRUCTURED-123",
                    Confidence = 0.99,
                    PageNumber = 1
                }
            ]
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.InvoiceNumber, "STRUCTURED-123");
    }

    [Fact]
    public void Extract_LabelOnPreviousLine_UsesNearbyTextHeuristic()
    {
        var ocr = OcrFromLines(
            "Nhà cung cấp:",
            "CÔNG TY CỔ PHẦN SAO MAI",
            "Mã số thuế:",
            "0301234567",
            "Ngày hóa đơn",
            "05-06-2026",
            "Tổng cộng",
            "1.234.567");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierName, "CÔNG TY CỔ PHẦN SAO MAI");
        AssertField(fields, FieldName.SupplierTaxCode, "0301234567");
        AssertField(fields, FieldName.InvoiceDate, "05-06-2026");
        AssertField(fields, FieldName.TotalAmount, "1.234.567");
    }

    [Fact]
    public void Extract_UnlabeledMerchantNameAboveAddressAndPhone_UsesTopLineHeuristic()
    {
        var ocr = OcrFromLines(
            "272 HÀ HUY TẬP - TP. HÀ TĨNH",
            "0911586768",
            "HÓA ĐƠN BÁN HÀNG",
            "MOTA CAFE",
            "Ngày 17/11/18");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierName, "MOTA CAFE");
    }

    [Fact]
    public void Extract_BareTongLabel_ExtractsTotalAmount()
    {
        var ocr = OcrFromLines(
            "Tiền hàng: 95.000",
            "Tổng: 85.000");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SubtotalAmount, "95.000");
        AssertField(fields, FieldName.TotalAmount, "85.000");
    }

    [Fact]
    public void Extract_CompetingTotalKeywords_PrefersStrongerKeywordOverWeakerOne()
    {
        var ocr = OcrFromLines(
            "Tổng thanh toán: 60.000",
            "Tổng: 50.000");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.TotalAmount, "60.000");
    }

    private static OcrResult OcrFromLines(params string[] lines)
    {
        var ocrLines = lines
            .Select((text, index) => new OcrLineResult
            {
                LineNumber = index + 1,
                Text = text,
                Confidence = 0.95,
                BoundingBox = BoundingBox.FromRect(0, index * 0.3, 8, 0.25)
            })
            .ToList();

        var page = new OcrPageResult
        {
            PageNumber = 1,
            FullText = string.Join('\n', lines),
            Confidence = 0.95,
            Lines = ocrLines
        };

        return new OcrResult
        {
            Success = true,
            FullText = page.FullText,
            Pages = [page],
            Confidence = 0.95,
            PageCount = 1
        };
    }

    private static void AssertField(
        IEnumerable<DocumentOCR.Domain.Entities.ExtractedField> fields,
        FieldName fieldName,
        string expected)
    {
        var field = fields.SingleOrDefault(f => f.FieldName == fieldName.ToString());
        Assert.NotNull(field);
        Assert.Equal(expected, field.RawValue);
        Assert.True(field.Confidence is >= 0 and <= 1);
    }
}
