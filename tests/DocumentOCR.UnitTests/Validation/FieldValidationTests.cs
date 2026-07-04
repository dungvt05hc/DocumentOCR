using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Validation;

public class FieldValidationTests
{
    private readonly FieldValidationService _sut = new();
    private static readonly Guid DocId = Guid.NewGuid();

    private static ExtractedField Field(string name, string? normalized, double? confidence = 0.95) =>
        new()
        {
            DocumentId = DocId,
            FieldName = name,
            RawValue = normalized,
            NormalizedValue = normalized,
            Confidence = confidence
        };

    private static List<ExtractedField> ValidFields() =>
    [
        Field(nameof(FieldName.SupplierName), "CONG TY ABC"),
        Field(nameof(FieldName.InvoiceNumber), "0001234"),
        Field(nameof(FieldName.InvoiceDate), "2024-12-31"),
        Field(nameof(FieldName.TotalAmount), "1236767"),
        Field(nameof(FieldName.SubtotalAmount), "1030639"),
        Field(nameof(FieldName.VatAmount), "206128"),
        Field(nameof(FieldName.SupplierTaxCode), "0100109106"),
        Field(nameof(FieldName.DocumentType), nameof(DocumentType.Invoice))
    ];

    [Fact]
    public void Validate_AllRequiredPresent_NoRequiredFieldWarnings()
    {
        var warnings = _sut.Validate(DocId, ValidFields());

        Assert.DoesNotContain(warnings, w => w.WarningCode == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public void Validate_MissingSupplierName_ProducesHighSeverityWarning()
    {
        var fields = ValidFields().Where(f => f.FieldName != nameof(FieldName.SupplierName)).ToList();

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.SupplierName) && w.Severity == ValidationSeverity.High);
    }

    [Fact]
    public void Validate_MissingTotalAmount_ProducesHighSeverityWarning()
    {
        var fields = ValidFields().Where(f => f.FieldName != nameof(FieldName.TotalAmount)).ToList();

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.TotalAmount) && w.Severity == ValidationSeverity.High);
    }

    [Fact]
    public void Validate_MissingSupplierTaxCodeForInvoice_ProducesHighSeverityWarning()
    {
        var fields = ValidFields().Where(f => f.FieldName != nameof(FieldName.SupplierTaxCode)).ToList();

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.SupplierTaxCode)
            && w.WarningCode == "REQUIRED_FIELD_MISSING"
            && w.Severity == ValidationSeverity.High);
    }

    [Fact]
    public void Validate_MissingSupplierTaxCodeForReceipt_DoesNotProduceTaxCodeRequiredWarning()
    {
        var fields = ValidFields().Where(f => f.FieldName != nameof(FieldName.SupplierTaxCode)).ToList();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.DocumentType));
        fields.Add(Field(nameof(FieldName.DocumentType), nameof(DocumentType.Receipt)));

        var warnings = _sut.Validate(DocId, fields);

        Assert.DoesNotContain(warnings, w =>
            w.FieldName == nameof(FieldName.SupplierTaxCode) && w.WarningCode == "REQUIRED_FIELD_MISSING");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1000")]
    [InlineData("abc")]
    public void Validate_InvalidTotalAmount_ProducesError(string value)
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.TotalAmount));
        fields.Add(Field(nameof(FieldName.TotalAmount), value));

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.TotalAmount) && w.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_SubPlusVatMatchesTotal_NoConsistencyWarning()
    {
        var fields = new List<ExtractedField>
        {
            Field(nameof(FieldName.SupplierName), "X"),
            Field(nameof(FieldName.InvoiceNumber), "1"),
            Field(nameof(FieldName.InvoiceDate), "2024-01-01"),
            Field(nameof(FieldName.SubtotalAmount), "1000000"),
            Field(nameof(FieldName.VatAmount), "100000"),
            Field(nameof(FieldName.TotalAmount), "1100000"),
            Field(nameof(FieldName.SupplierTaxCode), "0100109106")
        };

        var warnings = _sut.Validate(DocId, fields);

        Assert.DoesNotContain(warnings, w => w.WarningCode == "AMOUNT_CONSISTENCY_MISMATCH");
    }

    [Fact]
    public void Validate_SubPlusVatDoesNotMatchTotal_ProducesWarning()
    {
        var fields = new List<ExtractedField>
        {
            Field(nameof(FieldName.SupplierName), "X"),
            Field(nameof(FieldName.InvoiceNumber), "1"),
            Field(nameof(FieldName.InvoiceDate), "2024-01-01"),
            Field(nameof(FieldName.SubtotalAmount), "1000000"),
            Field(nameof(FieldName.VatAmount), "100000"),
            Field(nameof(FieldName.TotalAmount), "2000000"),
            Field(nameof(FieldName.SupplierTaxCode), "0100109106")
        };

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w => w.WarningCode == "AMOUNT_CONSISTENCY_MISMATCH");
    }

    [Fact]
    public void Validate_VietnameseMoneyFormats_NoMoneyWarnings()
    {
        var fields = new List<ExtractedField>
        {
            Field(nameof(FieldName.SupplierName), "X"),
            Field(nameof(FieldName.InvoiceNumber), "1"),
            Field(nameof(FieldName.InvoiceDate), "2024-01-01"),
            Field(nameof(FieldName.SupplierTaxCode), "0100109106"),
            new()
            {
                DocumentId = DocId,
                FieldName = nameof(FieldName.SubtotalAmount),
                RawValue = "1.000.000 VND",
                Confidence = 0.95
            },
            new()
            {
                DocumentId = DocId,
                FieldName = nameof(FieldName.VatAmount),
                RawValue = "100 000",
                Confidence = 0.95
            },
            new()
            {
                DocumentId = DocId,
                FieldName = nameof(FieldName.TotalAmount),
                RawValue = "₫1.100.000",
                Confidence = 0.95
            }
        };

        var warnings = _sut.Validate(DocId, fields);

        Assert.DoesNotContain(warnings, w => w.WarningCode == "INVALID_TOTAL_AMOUNT");
        Assert.DoesNotContain(warnings, w => w.WarningCode == "AMOUNT_CONSISTENCY_MISMATCH");
    }

    [Theory]
    [InlineData("0100109106")]
    [InlineData("0100109106001")]
    public void Validate_ValidTaxCode_NoWarning(string code)
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        fields.Add(Field(nameof(FieldName.SupplierTaxCode), code));

        var warnings = _sut.Validate(DocId, fields);

        Assert.DoesNotContain(warnings, w => w.FieldName == nameof(FieldName.SupplierTaxCode));
    }

    [Theory]
    [InlineData("0100109")]
    [InlineData("ABCDE12345")]
    public void Validate_InvalidTaxCode_ProducesWarning(string code)
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        fields.Add(Field(nameof(FieldName.SupplierTaxCode), code));

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w => w.FieldName == nameof(FieldName.SupplierTaxCode));
    }

    [Fact]
    public void Validate_LowConfidenceField_ProducesInfoWarning()
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.SupplierName));
        fields.Add(Field(nameof(FieldName.SupplierName), "CONG TY ABC", confidence: 0.5));

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.SupplierName) && w.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public void Validate_HighConfidenceField_NoLowConfidenceWarning()
    {
        var warnings = _sut.Validate(DocId, ValidFields());

        Assert.DoesNotContain(warnings, w => w.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public void Validate_InvalidInvoiceDate_ProducesWarning()
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.InvoiceDate));
        fields.Add(Field(nameof(FieldName.InvoiceDate), "99/99/9999"));

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.InvoiceDate) && w.WarningCode == "INVALID_INVOICE_DATE");
    }

    [Fact]
    public void Validate_InvoiceDateMoreThanThirtyDaysInFuture_ProducesWarning()
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.InvoiceDate));
        fields.Add(Field(nameof(FieldName.InvoiceDate), DateTime.UtcNow.AddDays(31).ToString("yyyy-MM-dd")));

        var warnings = _sut.Validate(DocId, fields);

        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.InvoiceDate)
            && w.WarningCode == "INVOICE_DATE_TOO_FAR_IN_FUTURE");
    }
}
