using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Validation;

public class FieldValidationTests
{
    private readonly FieldValidationService _sut = new();
    private static readonly Guid DocId = Guid.NewGuid();

    // ── Helper ────────────────────────────────────────────────────────────────────

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
        Field(nameof(FieldName.SupplierName), "CÔNG TY ABC"),
        Field(nameof(FieldName.InvoiceNumber), "0001234"),
        Field(nameof(FieldName.InvoiceDate), "2024-12-31"),
        Field(nameof(FieldName.TotalAmount), "1234567"),
        Field(nameof(FieldName.SubtotalAmount), "1030639"),
        Field(nameof(FieldName.VatAmount), "206128"),     // 1030639 + 206128 = 1236767 — within 2%
        Field(nameof(FieldName.SupplierTaxCode), "0100109106")
    ];

    // ── Required fields ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllRequiredPresent_NoRequiredFieldWarnings()
    {
        var warnings = _sut.Validate(DocId, ValidFields());
        Assert.DoesNotContain(warnings, w => w.Message.Contains("missing or empty"));
    }

    [Fact]
    public void Validate_MissingSupplierName_ProducesWarning()
    {
        var fields = ValidFields().Where(f => f.FieldName != nameof(FieldName.SupplierName)).ToList();
        var warnings = _sut.Validate(DocId, fields);
        Assert.Contains(warnings, w => w.FieldName == nameof(FieldName.SupplierName));
    }

    [Fact]
    public void Validate_MissingTotalAmount_ProducesWarning()
    {
        var fields = ValidFields().Where(f => f.FieldName != nameof(FieldName.TotalAmount)).ToList();
        var warnings = _sut.Validate(DocId, fields);
        Assert.Contains(warnings, w => w.FieldName == nameof(FieldName.TotalAmount));
    }

    // ── TotalAmount must be positive ──────────────────────────────────────────────

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

    // ── SubtotalAmount + VatAmount ≈ TotalAmount ──────────────────────────────────

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
            Field(nameof(FieldName.TotalAmount), "1100000")      // exact match
        };

        var warnings = _sut.Validate(DocId, fields);
        Assert.DoesNotContain(warnings, w =>
            w.Message.Contains("SubtotalAmount") && w.Message.Contains("VatAmount"));
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
            Field(nameof(FieldName.TotalAmount), "2000000")      // clearly wrong
        };

        var warnings = _sut.Validate(DocId, fields);
        Assert.Contains(warnings, w => w.Message.Contains("SubtotalAmount"));
    }

    // ── Tax code ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0100109106")]    // valid 10-digit
    [InlineData("0100109106001")] // valid 13-digit
    public void Validate_ValidTaxCode_NoWarning(string code)
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        fields.Add(Field(nameof(FieldName.SupplierTaxCode), code));

        var warnings = _sut.Validate(DocId, fields);
        Assert.DoesNotContain(warnings, w => w.FieldName == nameof(FieldName.SupplierTaxCode));
    }

    [Theory]
    [InlineData("0100109")]          // only 7 digits
    [InlineData("ABCDE12345")]       // contains letters after normalization
    public void Validate_InvalidTaxCode_ProducesWarning(string code)
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        fields.Add(Field(nameof(FieldName.SupplierTaxCode), code));

        var warnings = _sut.Validate(DocId, fields);
        Assert.Contains(warnings, w => w.FieldName == nameof(FieldName.SupplierTaxCode));
    }

    // ── Low confidence ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_LowConfidenceField_ProducesInfoWarning()
    {
        var fields = ValidFields();
        fields.RemoveAll(f => f.FieldName == nameof(FieldName.SupplierName));
        fields.Add(Field(nameof(FieldName.SupplierName), "CÔNG TY ABC", confidence: 0.5));

        var warnings = _sut.Validate(DocId, fields);
        Assert.Contains(warnings, w =>
            w.FieldName == nameof(FieldName.SupplierName) && w.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public void Validate_HighConfidenceField_NoLowConfidenceWarning()
    {
        var fields = ValidFields(); // all have 0.95 confidence by default
        var warnings = _sut.Validate(DocId, fields);
        Assert.DoesNotContain(warnings, w => w.Severity == ValidationSeverity.Info);
    }
}
