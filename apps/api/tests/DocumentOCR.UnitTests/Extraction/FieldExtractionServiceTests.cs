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
        AssertField(fields, FieldName.DocumentType, nameof(DocumentType.VatInvoice));
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
        var ocr = new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = "Test",
            FullText = """
                HÓA ĐƠN BÁN HÀNG
                MST: 0100109106
                Số HĐ: 0005678
                Ngày: 1/7/2026
                Cộng tiền hàng: 2 000 000
                Thuế GTGT: 200 000
                Tổng tiền: 2 200 000 VND
                """,
            AverageConfidence = 0.8,
            PageCount = 1
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierTaxCode, "0100109106");
        AssertField(fields, FieldName.InvoiceNumber, "0005678");
        AssertField(fields, FieldName.InvoiceDate, "1/7/2026");
        AssertField(fields, FieldName.SubtotalAmount, "2 000 000");
        AssertField(fields, FieldName.VatAmount, "200 000");
        AssertField(fields, FieldName.TotalAmount, "2 200 000 VND");
        // "HÓA ĐƠN BÁN HÀNG" is a POS/sales receipt title, not a VAT invoice, despite the
        // "hóa đơn" substring — it's a distinct category (PosReceipt) from a generic Receipt.
        AssertField(fields, FieldName.DocumentType, nameof(DocumentType.PosReceipt));
    }

    [Fact]
    public void Extract_StructuredFieldHasHigherConfidence_KeepsStructuredValue()
    {
        var ocr = OcrFromLines(
            "Số hóa đơn: OCR-LOW",
            "Tổng thanh toán: 9.999");

        ocr = ocr with
        {
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

    [Fact]
    public void Extract_AppOrderReceiptScreenshot_DetectsAppReceiptScreenshotCategory()
    {
        var ocr = OcrFromLines(
            "ShopeeFood",
            "Mã đơn hàng: SPF123456",
            "Tổng cộng: 125.000");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.DocumentType, nameof(DocumentCategory.AppReceiptScreenshot));
    }

    [Fact]
    public void Extract_EnglishCommercialInvoice_DetectsCommercialInvoiceCategory()
    {
        var ocr = OcrFromLines(
            "COMMERCIAL INVOICE",
            "Bill To: Acme Corp",
            "Invoice Number: INV-2026-001",
            "Total: USD 1,200.00");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.DocumentType, nameof(DocumentCategory.CommercialInvoice));
    }

    [Fact]
    public void Extract_EnglishTaxInvoiceWithPurchaseOrder_DetectsInternationalInvoiceCategory()
    {
        var ocr = OcrFromLines(
            "TAX INVOICE",
            "Purchase Order: PO-9981",
            "Bill To: Acme Corp",
            "Total: USD 800.00");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.DocumentType, nameof(DocumentCategory.InternationalInvoice));
    }

    [Fact]
    public void Extract_InternationalInvoiceTemplate1Style_ExtractsHeaderFieldsCorrectly()
    {
        // Mirrors the Template1_Instance0-style international invoice bug report: an English
        // "Date:"/"Due Date:"/"PO Number:" invoice whose vendor-name and invoice-number
        // extraction were previously wrong (see FieldExtractionService.cs history for the fixes).
        var ocr = OcrFromLines(
            "Date: 20-Mar-2008",
            "Northwind Traders",
            "www.ThompsonandSons.org",
            "TAX INVOICE",
            "Invoice Number: INV-2008-0035",
            "Due Date: 16-Oct-2016",
            "PO Number: 35",
            "SUB_TOTAL: 725.30 EUR",
            "TAX:VAT (3.88%): 28.18 EUR",
            "TOTAL: 734.33 EUR");

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierName, "Northwind Traders");
        AssertField(fields, FieldName.InvoiceNumber, "INV-2008-0035");
        AssertField(fields, FieldName.InvoiceDate, "20-Mar-2008");
        AssertStringField(fields, "DueDate", "16-Oct-2016");
        AssertStringField(fields, "PONumber", "35");
        AssertField(fields, FieldName.SubtotalAmount, "725.30");
        AssertField(fields, FieldName.VatAmount, "28.18");
        AssertField(fields, FieldName.TotalAmount, "734.33");
        AssertField(fields, FieldName.Currency, "EUR");
    }

    [Fact]
    public void Extract_WebsiteLineLooksLikeInvoiceNumberKeyword_NeverUsesWebsiteAsInvoiceNumber()
    {
        // "www.ThompsonandSons.org" normalizes to "www.thompsonandsons.org", which contains the
        // bare Vietnamese "so" (số) keyword as a false-positive substring — without the URL
        // rejection filter, this line alone would previously become the InvoiceNumber value.
        var ocr = OcrFromLines("www.ThompsonandSons.org", "Unrelated filler content");

        var fields = _sut.Extract(DocumentId, ocr);

        Assert.DoesNotContain(fields, f => f.FieldName == nameof(FieldName.InvoiceNumber));
    }

    private static NormalizedOcrDocument OcrFromLines(params string[] lines)
    {
        var ocrLines = lines
            .Select((text, index) => new OcrLine
            {
                LineNumber = index + 1,
                Text = text,
                PageNumber = 1,
                Confidence = 0.95,
                BoundingBox = BoundingBox.FromRect(0, index * 0.3, 8, 0.25)
            })
            .ToList();

        var page = new OcrPage
        {
            PageNumber = 1,
            FullText = string.Join('\n', lines),
            Confidence = 0.95,
            Lines = ocrLines
        };

        return new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = "Test",
            FullText = page.FullText,
            Pages = [page],
            AverageConfidence = 0.95,
            PageCount = 1
        };
    }

    private static void AssertField(
        IEnumerable<DocumentOCR.Domain.Entities.ExtractedField> fields,
        FieldName fieldName,
        string expected)
    {
        AssertStringField(fields, fieldName.ToString(), expected);
    }

    /// <summary>For dynamic-profile-only field keys (e.g. "DueDate", "PONumber") that have no entry in the legacy <see cref="FieldName"/> enum.</summary>
    private static void AssertStringField(
        IEnumerable<DocumentOCR.Domain.Entities.ExtractedField> fields,
        string fieldName,
        string expected)
    {
        var field = fields.SingleOrDefault(f => f.FieldName == fieldName);
        Assert.NotNull(field);
        Assert.Equal(expected, field.RawValue);
        Assert.True(field.Confidence is >= 0 and <= 1);
    }
}
