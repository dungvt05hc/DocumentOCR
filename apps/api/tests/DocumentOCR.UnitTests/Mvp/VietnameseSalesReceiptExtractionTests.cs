using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Processing;
using DocumentOCR.Infrastructure.Profiles;
using Xunit;

namespace DocumentOCR.UnitTests.Mvp;

/// <summary>
/// A real Vietnamese POS sales-receipt sample (no explicit field labels for the merchant name,
/// a bare "Tổng:" total, and a "HÓA ĐƠN BÁN HÀNG" title) that previously failed to extract
/// SupplierName/TotalAmount and was misclassified as a VAT invoice requiring a tax code.
/// </summary>
public class VietnameseSalesReceiptExtractionTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();

    [Fact]
    public void ExtractAndNormalize_MotaCafeReceipt_ReturnsExpectedFields()
    {
        var fields = ExtractAndNormalize();

        AssertNormalized(fields, FieldName.SupplierName, "MOTA CAFE");
        AssertNormalized(fields, FieldName.InvoiceNumber, "111800005");
        AssertNormalized(fields, FieldName.InvoiceDate, "2018-11-17");
        AssertNormalized(fields, FieldName.SubtotalAmount, "95000");
        AssertNormalized(fields, FieldName.TotalAmount, "85000");
        AssertNormalized(fields, FieldName.Currency, "VND");
        AssertNormalized(fields, FieldName.DocumentType, nameof(DocumentType.PosReceipt));
    }

    [Fact]
    public void ExtractAndNormalize_MotaCafeReceipt_LeavesSupplierTaxCodeEmpty()
    {
        var fields = ExtractAndNormalize();

        var taxCode = fields.SingleOrDefault(f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        Assert.True(taxCode is null || string.IsNullOrWhiteSpace(taxCode.NormalizedValue));
    }

    [Fact]
    public void Validate_MotaCafeReceipt_DoesNotRequireSupplierTaxCode()
    {
        var fields = ExtractAndNormalize();
        var sut = new FieldValidationService(new DocumentProfileCatalog());

        var warnings = sut.Validate(DocumentId, fields);

        Assert.DoesNotContain(warnings, w =>
            w.FieldName == nameof(FieldName.SupplierTaxCode) && w.Severity == ValidationSeverity.High);
    }

    [Fact]
    public void Validate_MotaCafeReceipt_DoesNotFlagTotalAmountAsMissing()
    {
        var fields = ExtractAndNormalize();
        var sut = new FieldValidationService(new DocumentProfileCatalog());

        var warnings = sut.Validate(DocumentId, fields);

        Assert.DoesNotContain(warnings, w =>
            w.FieldName == nameof(FieldName.TotalAmount) && w.WarningCode == "REQUIRED_FIELD_MISSING");
    }

    private static IReadOnlyList<ExtractedField> ExtractAndNormalize()
    {
        var extraction = new FieldExtractionService();
        var normalization = new FieldNormalizationService();
        var ocrResult = VietnameseMvpTestData.OcrFromLines(VietnameseMvpTestData.MotaCafeReceiptLines);

        var fields = extraction.Extract(DocumentId, ocrResult);
        normalization.NormalizeFields(fields);
        return fields;
    }

    private static void AssertNormalized(
        IEnumerable<ExtractedField> fields,
        FieldName fieldName,
        string expectedValue)
    {
        var field = fields.SingleOrDefault(f => f.FieldName == fieldName.ToString());
        Assert.NotNull(field);
        Assert.Equal(expectedValue, field.NormalizedValue);
    }
}
