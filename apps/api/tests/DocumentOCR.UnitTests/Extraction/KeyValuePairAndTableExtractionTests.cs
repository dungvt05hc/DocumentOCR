using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Application.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Extraction;

/// <summary>
/// Proves the KeyValuePairs/Tables extraction paths added to support Azure prebuilt-layout —
/// which, unlike prebuilt-invoice/receipt, never populates <see cref="NormalizedOcrDocument.Fields"/>
/// and previously left field extraction with nothing but blind full-text regex to work with.
/// </summary>
public class KeyValuePairAndTableExtractionTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private readonly FieldExtractionService _sut = new();

    [Fact]
    public void Extract_OnlyKeyValuePairsNoStructuredFieldsOrLines_ExtractsCanonicalFields()
    {
        // Simulates a prebuilt-layout response: no Documents[].Fields, no recognizable line
        // text (as if OCR text came back as a single opaque blob) — KeyValuePairs is the only
        // usable signal, exactly the scenario prebuilt-layout + keyValuePairs is meant to cover.
        var ocr = new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = "AzureDocumentIntelligence",
            ModelId = "prebuilt-layout",
            PageCount = 1,
            KeyValuePairs =
            [
                new() { KeyText = "Đơn vị bán hàng", ValueText = "CÔNG TY TNHH ABC", Confidence = 0.93, PageNumber = 1 },
                new() { KeyText = "Mã số thuế", ValueText = "0100109106", Confidence = 0.95, PageNumber = 1 },
                new() { KeyText = "Số hóa đơn", ValueText = "0001234", Confidence = 0.9, PageNumber = 1 },
                new() { KeyText = "Ngày hóa đơn", ValueText = "31/12/2024", Confidence = 0.9, PageNumber = 1 },
                new() { KeyText = "Tổng thanh toán", ValueText = "1.236.767", Confidence = 0.94, PageNumber = 1 }
            ]
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.SupplierName, "CÔNG TY TNHH ABC");
        AssertField(fields, FieldName.SupplierTaxCode, "0100109106");
        AssertField(fields, FieldName.InvoiceNumber, "0001234");
        AssertField(fields, FieldName.InvoiceDate, "31/12/2024");
        AssertField(fields, FieldName.TotalAmount, "1.236.767");

        var total = fields.Single(f => f.FieldName == nameof(FieldName.TotalAmount));
        Assert.Equal("KeyValuePair", total.ExtractionMethod);
        Assert.Equal("Tổng thanh toán", total.ProviderFieldName);
    }

    [Fact]
    public void Extract_KeyValuePairOutranksWeakerLineCandidate_KeepsKeyValuePairValue()
    {
        // A generic bare "Tổng" line (weak keyword) coexists with a strong, well-labeled
        // KeyValuePair for the same field — the KVP should win.
        var lines = new[] { "Tổng: 50.000" };
        var ocr = OcrFromLines(lines) with
        {
            KeyValuePairs = [new() { KeyText = "Tổng thanh toán", ValueText = "85.000", Confidence = 0.92, PageNumber = 1 }]
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.TotalAmount, "85.000");
    }

    [Fact]
    public void Extract_TotalInWordsKeyValuePair_DoesNotOverrideNumericTotalLine()
    {
        // Regression: Azure layout parsed "Total in words: seven hundred and thirty-four point
        // three three" as a KeyValuePair. Its key contains the bare "total" keyword, so it used
        // to be misclassified as TotalAmount with the spelled-out text as the raw value — even
        // though the real "TOTAL : 734.33 EUR" line was also present.
        var lines = new[] { "TOTAL : 734.33 EUR" };
        var ocr = OcrFromLines(lines) with
        {
            KeyValuePairs =
            [
                new()
                {
                    KeyText = "Total in words",
                    ValueText = "seven hundred and thirty-four point three three",
                    Confidence = 0.9,
                    PageNumber = 1
                }
            ]
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.TotalAmount, "734.33");
    }

    [Fact]
    public void Extract_TableFooterRow_ExtractsTotalAmountFromTableCell()
    {
        // No lines, no KeyValuePairs, no structured Fields at all — only a layout table whose
        // last row is a "Tổng cộng" / amount pair, as prebuilt-layout returns for tabular receipts.
        var ocr = new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = "AzureDocumentIntelligence",
            ModelId = "prebuilt-layout",
            PageCount = 1,
            Tables =
            [
                new()
                {
                    PageNumber = 1,
                    RowCount = 3,
                    ColumnCount = 2,
                    Cells =
                    [
                        new() { RowIndex = 0, ColumnIndex = 0, Text = "Mặt hàng" },
                        new() { RowIndex = 0, ColumnIndex = 1, Text = "Thành tiền" },
                        new() { RowIndex = 1, ColumnIndex = 0, Text = "Trà sữa" },
                        new() { RowIndex = 1, ColumnIndex = 1, Text = "30.000" },
                        new() { RowIndex = 2, ColumnIndex = 0, Text = "Tổng cộng" },
                        new() { RowIndex = 2, ColumnIndex = 1, Text = "85.000" }
                    ]
                }
            ]
        };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.TotalAmount, "85.000");
        var total = fields.Single(f => f.FieldName == nameof(FieldName.TotalAmount));
        Assert.Equal("TableFooter", total.ExtractionMethod);
        Assert.Equal("Table", total.SourceType);
    }

    [Theory]
    [InlineData("HÓA ĐƠN GIÁ TRỊ GIA TĂNG\nSố: 1", nameof(DocumentType.VatInvoice))]
    [InlineData("HÓA ĐƠN BÁN HÀNG\nSố: 1", nameof(DocumentType.PosReceipt))]
    [InlineData("NHÀ HÀNG SEN VIỆT\nSố: 1", nameof(DocumentType.RestaurantBill))]
    public void Extract_TitleMarkers_ClassifyIntoSpecificCategory(string fullText, string expectedCategory)
    {
        var ocr = new NormalizedOcrDocument { Success = true, ProviderName = "Test", FullText = fullText, PageCount = 1 };

        var fields = _sut.Extract(DocumentId, ocr);

        AssertField(fields, FieldName.DocumentType, expectedCategory);
    }

    private static NormalizedOcrDocument OcrFromLines(params string[] lines)
    {
        var ocrLines = lines
            .Select((text, index) => new OcrLine
            {
                LineNumber = index + 1,
                Text = text,
                PageNumber = 1,
                Confidence = 0.9,
                BoundingBox = BoundingBox.FromRect(0, index * 0.3, 8, 0.25)
            })
            .ToList();

        var page = new OcrPage
        {
            PageNumber = 1,
            FullText = string.Join('\n', lines),
            Confidence = 0.9,
            Lines = ocrLines
        };

        return new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = "Test",
            FullText = page.FullText,
            Pages = [page],
            AverageConfidence = 0.9,
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
    }
}
