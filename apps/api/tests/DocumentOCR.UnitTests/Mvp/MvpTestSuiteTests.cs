using ClosedXML.Excel;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Export;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Profiles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentOCR.UnitTests.Mvp;

public class MvpTestSuiteTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();

    [Theory]
    [InlineData("1.000.000", 1000000)]
    [InlineData("100.000", 100000)]
    [InlineData("1.100.000 VND", 1100000)]
    public void NormalizeCurrency_VietnameseInvoiceAmounts_ReturnsExpectedAmount(
        string rawValue,
        decimal expected)
    {
        var sut = new FieldNormalizationService();

        var result = sut.NormalizeCurrency(rawValue);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeDate_VietnameseInvoiceDate_ReturnsExpectedDate()
    {
        var sut = new FieldNormalizationService();

        var result = sut.NormalizeDate("02/07/2026");

        Assert.Equal(new DateOnly(2026, 7, 2), result);
    }

    [Fact]
    public void NormalizeTaxCode_VietnameseTaxCode_ReturnsDigitsOnly()
    {
        var sut = new FieldNormalizationService();

        var result = sut.NormalizeTaxCode("Mã số thuế: 0312345678");

        Assert.Equal("0312345678", result);
    }

    [Fact]
    public void ExtractAndNormalize_SampleVietnameseInvoice_ReturnsExpectedFields()
    {
        var extraction = new FieldExtractionService();
        var normalization = new FieldNormalizationService();
        var ocrResult = VietnameseMvpTestData.OcrFromLines(VietnameseMvpTestData.InvoiceLines);

        var fields = extraction.Extract(DocumentId, ocrResult);
        normalization.NormalizeFields(fields);

        AssertNormalized(fields, FieldName.SupplierTaxCode, "0312345678");
        AssertNormalized(fields, FieldName.InvoiceNumber, "000123");
        AssertNormalized(fields, FieldName.InvoiceDate, "2026-07-02");
        AssertNormalized(fields, FieldName.SubtotalAmount, "1000000");
        AssertNormalized(fields, FieldName.VatAmount, "100000");
        AssertNormalized(fields, FieldName.TotalAmount, "1100000");
        AssertNormalized(fields, FieldName.Currency, "VND");
    }

    [Fact]
    public void Validate_SampleVietnameseInvoice_DoesNotCreateVatMismatchWarning()
    {
        var fields = ExtractAndNormalize(VietnameseMvpTestData.InvoiceLines);
        var sut = new FieldValidationService(new DocumentProfileCatalog());

        var warnings = sut.Validate(DocumentId, fields);

        Assert.DoesNotContain(warnings, warning => warning.WarningCode == "AMOUNT_CONSISTENCY_MISMATCH");
        Assert.DoesNotContain(warnings, warning => warning.WarningCode == "INVALID_TOTAL_AMOUNT");
        Assert.DoesNotContain(warnings, warning => warning.WarningCode == "INVALID_TAX_CODE_LENGTH");
    }

    [Fact]
    public void Validate_VatTotalMismatch_CreatesWarning()
    {
        var fields = ExtractAndNormalize(VietnameseMvpTestData.InvoiceLines).ToList();
        var total = fields.Single(field => field.FieldName == nameof(FieldName.TotalAmount));
        total.NormalizedValue = "1200000";

        var sut = new FieldValidationService(new DocumentProfileCatalog());

        var warnings = sut.Validate(DocumentId, fields);

        Assert.Contains(warnings, warning => warning.WarningCode == "AMOUNT_CONSISTENCY_MISMATCH");
    }

    [Fact]
    public async Task AnalyzeAsync_FakeOcrProvider_ReturnsVietnameseInvoiceOcrResult()
    {
        var sut = new FakeOcrProvider();
        var input = new DocumentInput
        {
            Content = new MemoryStream([0x25, 0x50, 0x44, 0x46]),
            FileName = "sample.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 4
        };

        var result = await sut.AnalyzeAsync(input);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Pages);
        Assert.NotEmpty(result.Fields);
        Assert.Contains(result.Fields, field => field.FieldKey == "VendorTaxId");
        Assert.Contains(result.Fields, field => field.FieldKey == "InvoiceTotal");
    }

    [Fact]
    public async Task ExportAsync_VietnameseInvoiceAndReceipt_CreatesExpectedExcelStructure()
    {
        await using var db = CreateDbContext();
        var organization = new Organization
        {
            Name = "MVP Test Organization",
            Slug = "mvp-test-organization"
        };
        var invoice = VietnameseMvpTestData.InvoiceDocument(organization.Id);
        var receipt = VietnameseMvpTestData.ReceiptDocument(organization.Id);

        db.Organizations.Add(organization);
        db.Documents.AddRange(invoice, receipt);
        await db.SaveChangesAsync();

        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([invoice.Id, receipt.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var documents = workbook.Worksheet("Documents");
        var warnings = workbook.Worksheet("Warnings");

        Assert.Equal("Tên tệp", documents.Cell(1, 1).GetString());
        Assert.Equal("Mã số thuế", documents.Cell(1, 4).GetString());
        Assert.Equal("Tổng thanh toán", documents.Cell(1, 9).GetString());
        Assert.Equal("invoice-abc.pdf", documents.Cell(2, 1).GetString());
        Assert.Equal("0312345678", documents.Cell(2, 4).GetString());
        Assert.Equal(1100000m, documents.Cell(2, 9).GetValue<decimal>());
        Assert.Equal("receipt-minh-an.png", documents.Cell(3, 1).GetString());
        Assert.Equal("Warnings", warnings.Name);
        Assert.Equal("LOW_CONFIDENCE", warnings.Cell(2, 3).GetString());
    }

    [Fact]
    public void ExtractAndNormalize_SampleVietnameseReceipt_ReturnsReceiptFields()
    {
        var fields = ExtractAndNormalize(VietnameseMvpTestData.ReceiptLines);

        AssertNormalized(fields, FieldName.DocumentType, nameof(DocumentType.Receipt));
        AssertNormalized(fields, FieldName.SupplierTaxCode, "0312345678");
        AssertNormalized(fields, FieldName.InvoiceNumber, "RC-9988");
        AssertNormalized(fields, FieldName.InvoiceDate, "2026-07-03");
        AssertNormalized(fields, FieldName.TotalAmount, "550000");
        AssertNormalized(fields, FieldName.Currency, "VND");
    }

    private static IReadOnlyList<ExtractedField> ExtractAndNormalize(params string[] lines)
    {
        var extraction = new FieldExtractionService();
        var normalization = new FieldNormalizationService();
        var ocrResult = VietnameseMvpTestData.OcrFromLines(lines);
        var fields = extraction.Extract(DocumentId, ocrResult);
        normalization.NormalizeFields(fields);
        return fields;
    }

    private static void AssertNormalized(
        IEnumerable<ExtractedField> fields,
        FieldName fieldName,
        string expectedValue)
    {
        var field = fields.SingleOrDefault(field => field.FieldName == fieldName.ToString());
        Assert.NotNull(field);
        Assert.Equal(expectedValue, field.NormalizedValue);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
