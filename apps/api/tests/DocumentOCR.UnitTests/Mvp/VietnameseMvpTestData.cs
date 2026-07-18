using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.UnitTests.Mvp;

internal static class VietnameseMvpTestData
{
    public static readonly string[] InvoiceLines =
    [
        "CÔNG TY TNHH ABC",
        "Mã số thuế: 0312345678",
        "Số hóa đơn: 000123",
        "Ngày hóa đơn: 02/07/2026",
        "Cộng tiền hàng: 1.000.000",
        "Thuế GTGT: 100.000",
        "Tổng thanh toán: 1.100.000 VND"
    ];

    public static readonly string[] ReceiptLines =
    [
        "PHIẾU THU",
        "Cửa hàng Minh An",
        "MST: 0312345678",
        "Invoice No: RC-9988",
        "Invoice date: 03/07/2026",
        "Subtotal: 500.000",
        "VAT: 50.000",
        "Tổng cộng: 550.000 VND"
    ];

    /// <summary>
    /// A real POS sales-receipt shape: no explicit "Đơn vị bán hàng"/"Tổng cộng" labels, a bare
    /// "Tổng:" total line, and a "HÓA ĐƠN BÁN HÀNG" title (sales receipt, not a VAT invoice).
    /// </summary>
    public static readonly string[] MotaCafeReceiptLines =
    [
        "MOTA CAFE",
        "272 HÀ HUY TẬP - TP. HÀ TĨNH",
        "0911586768",
        "HÓA ĐƠN BÁN HÀNG",
        "Ngày 17/11/18",
        "Số: 111800005",
        "BÀN 02",
        "Thu ngân: Administrator",
        "Giờ vào: 17:17",
        "Mặt hàng SL Giá T tiền",
        "Gold Kiwi Green Tea (Trà Kiwi) 1 35.000 35.000",
        "Trà sữa vị hạt dẻ 1 30.000 30.000",
        "Trà sữa vị phúc bồn tử 1 30.000 30.000",
        "Tiền hàng: 95.000",
        "Giảm 10%: 10.000",
        "Tổng: 85.000"
    ];

    public static OcrResult OcrFromLines(params string[] lines)
    {
        var ocrLines = lines
            .Select((text, index) => new OcrLineResult
            {
                LineNumber = index + 1,
                Text = text,
                Confidence = 0.96,
                BoundingBox = BoundingBox.FromRect(0, index * 0.3, 8, 0.25)
            })
            .ToList();

        var page = new OcrPageResult
        {
            PageNumber = 1,
            FullText = string.Join('\n', lines),
            Confidence = 0.96,
            Lines = ocrLines
        };

        return new OcrResult
        {
            Success = true,
            FullText = page.FullText,
            Pages = [page],
            Confidence = 0.96,
            PageCount = 1
        };
    }

    public static Document InvoiceDocument(Guid organizationId)
    {
        var document = new Document
        {
            OrganizationId = organizationId,
            OriginalFileName = "invoice-abc.pdf",
            StoredFilePath = "2026/07/invoice-abc.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Reviewed,
            DocumentType = DocumentType.Invoice,
            CreatedAt = new DateTime(2026, 7, 2, 9, 30, 0, DateTimeKind.Utc)
        };

        document.Fields.Add(Field(document.Id, FieldName.SupplierName, "CÔNG TY TNHH ABC"));
        document.Fields.Add(Field(document.Id, FieldName.SupplierTaxCode, "0312345678"));
        document.Fields.Add(Field(document.Id, FieldName.InvoiceNumber, "000123"));
        document.Fields.Add(Field(document.Id, FieldName.InvoiceDate, "2026-07-02"));
        document.Fields.Add(Field(document.Id, FieldName.SubtotalAmount, "1000000"));
        document.Fields.Add(Field(document.Id, FieldName.VatAmount, "100000"));
        document.Fields.Add(Field(document.Id, FieldName.TotalAmount, "1100000"));
        document.Fields.Add(Field(document.Id, FieldName.Currency, "VND"));

        return document;
    }

    public static Document ReceiptDocument(Guid organizationId)
    {
        var document = new Document
        {
            OrganizationId = organizationId,
            OriginalFileName = "receipt-minh-an.png",
            StoredFilePath = "2026/07/receipt-minh-an.png",
            ContentType = "image/png",
            FileSizeBytes = 2048,
            Status = DocumentStatus.Processed,
            DocumentType = DocumentType.Receipt,
            CreatedAt = new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc)
        };

        document.Fields.Add(Field(document.Id, FieldName.SupplierName, "Cửa hàng Minh An"));
        document.Fields.Add(Field(document.Id, FieldName.SupplierTaxCode, "0312345678"));
        document.Fields.Add(Field(document.Id, FieldName.InvoiceNumber, "RC-9988"));
        document.Fields.Add(Field(document.Id, FieldName.InvoiceDate, "2026-07-03"));
        document.Fields.Add(Field(document.Id, FieldName.SubtotalAmount, "500000"));
        document.Fields.Add(Field(document.Id, FieldName.VatAmount, "50000"));
        document.Fields.Add(Field(document.Id, FieldName.TotalAmount, "550000"));
        document.Fields.Add(Field(document.Id, FieldName.Currency, "VND"));
        document.ValidationWarnings.Add(new ValidationWarning
        {
            DocumentId = document.Id,
            FieldName = nameof(FieldName.TotalAmount),
            WarningCode = "LOW_CONFIDENCE",
            Severity = ValidationSeverity.Info,
            Message = "Total amount has low confidence."
        });

        return document;
    }

    private static ExtractedField Field(Guid documentId, FieldName fieldName, string value) =>
        new()
        {
            DocumentId = documentId,
            FieldName = fieldName.ToString(),
            RawValue = value,
            NormalizedValue = value,
            Confidence = 0.96
        };
}
