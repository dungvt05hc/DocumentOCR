using System.Globalization;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Processing;

public class FieldValidationService : IFieldValidationService
{
    private const double LowConfidenceThreshold = 0.7;
    private const decimal TotalMatchTolerancePercent = 0.02m; // 2% tolerance

    private static readonly string[] RequiredFields =
    [
        nameof(FieldName.SupplierName),
        nameof(FieldName.InvoiceNumber),
        nameof(FieldName.InvoiceDate),
        nameof(FieldName.TotalAmount)
    ];

    public IReadOnlyList<ValidationWarning> Validate(Guid documentId, IEnumerable<ExtractedField> fields)
    {
        var fieldList = fields.ToList();
        var warnings = new List<ValidationWarning>();

        ValidateRequiredFields(documentId, fieldList, warnings);
        ValidateTaxCode(documentId, fieldList, warnings);
        ValidateTotalAmount(documentId, fieldList, warnings);
        ValidateAmountConsistency(documentId, fieldList, warnings);
        ValidateLowConfidence(documentId, fieldList, warnings);

        return warnings;
    }

    private static void ValidateRequiredFields(
        Guid documentId, List<ExtractedField> fields, List<ValidationWarning> warnings)
    {
        foreach (var required in RequiredFields)
        {
            var field = fields.FirstOrDefault(f => f.FieldName == required);
            if (field is null || string.IsNullOrWhiteSpace(field.NormalizedValue))
            {
                warnings.Add(new ValidationWarning
                {
                    DocumentId = documentId,
                    FieldName = required,
                    WarningCode = "REQUIRED_FIELD_MISSING",
                    Severity = ValidationSeverity.Warning,
                    Message = $"Required field '{required}' is missing or empty."
                });
            }
        }
    }

    private static void ValidateTaxCode(
        Guid documentId, List<ExtractedField> fields, List<ValidationWarning> warnings)
    {
        var taxCode = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        if (taxCode is null || string.IsNullOrWhiteSpace(taxCode.NormalizedValue)) return;

        // Vietnamese tax codes are 10 or 13 digits
        if (!taxCode.NormalizedValue.All(char.IsDigit))
        {
            warnings.Add(new ValidationWarning
            {
                DocumentId = documentId,
                FieldName = nameof(FieldName.SupplierTaxCode),
                WarningCode = "INVALID_TAX_CODE_FORMAT",
                Severity = ValidationSeverity.Warning,
                Message = $"Tax code '{taxCode.NormalizedValue}' contains non-digit characters after normalization."
            });
        }
        else if (taxCode.NormalizedValue.Length is not (10 or 13))
        {
            warnings.Add(new ValidationWarning
            {
                DocumentId = documentId,
                FieldName = nameof(FieldName.SupplierTaxCode),
                WarningCode = "INVALID_TAX_CODE_LENGTH",
                Severity = ValidationSeverity.Warning,
                Message = $"Tax code '{taxCode.NormalizedValue}' has {taxCode.NormalizedValue.Length} digits; expected 10 or 13 for a Vietnamese tax code."
            });
        }
    }

    private static void ValidateTotalAmount(
        Guid documentId, List<ExtractedField> fields, List<ValidationWarning> warnings)
    {
        var totalField = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.TotalAmount));
        if (totalField is null || string.IsNullOrWhiteSpace(totalField.NormalizedValue)) return;

        if (!decimal.TryParse(totalField.NormalizedValue, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var total) || total <= 0)
        {
            warnings.Add(new ValidationWarning
            {
                DocumentId = documentId,
                FieldName = nameof(FieldName.TotalAmount),
                WarningCode = "INVALID_TOTAL_AMOUNT",
                Severity = ValidationSeverity.Error,
                Message = $"TotalAmount '{totalField.NormalizedValue}' must be a positive number."
            });
        }
    }

    private static void ValidateAmountConsistency(
        Guid documentId, List<ExtractedField> fields, List<ValidationWarning> warnings)
    {
        var subtotalField = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.SubtotalAmount));
        var vatField = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.VatAmount));
        var totalField = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.TotalAmount));

        if (subtotalField is null || vatField is null || totalField is null) return;
        if (string.IsNullOrWhiteSpace(subtotalField.NormalizedValue)
            || string.IsNullOrWhiteSpace(vatField.NormalizedValue)
            || string.IsNullOrWhiteSpace(totalField.NormalizedValue)) return;

        if (!decimal.TryParse(subtotalField.NormalizedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var sub)) return;
        if (!decimal.TryParse(vatField.NormalizedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var vat)) return;
        if (!decimal.TryParse(totalField.NormalizedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var total)) return;

        var expected = sub + vat;
        var tolerance = total * TotalMatchTolerancePercent;

        if (Math.Abs(expected - total) > tolerance)
        {
            warnings.Add(new ValidationWarning
            {
                DocumentId = documentId,
                FieldName = nameof(FieldName.TotalAmount),
                WarningCode = "AMOUNT_CONSISTENCY_MISMATCH",
                Severity = ValidationSeverity.Warning,
                Message = $"SubtotalAmount ({sub}) + VatAmount ({vat}) = {expected}, but TotalAmount is {total}. Difference exceeds 2%."
            });
        }
    }

    private static void ValidateLowConfidence(
        Guid documentId, List<ExtractedField> fields, List<ValidationWarning> warnings)
    {
        foreach (var field in fields.Where(f =>
                     f.Confidence.HasValue && f.Confidence < LowConfidenceThreshold))
        {
            warnings.Add(new ValidationWarning
            {
                DocumentId = documentId,
                FieldName = field.FieldName,
                WarningCode = "LOW_CONFIDENCE",
                Severity = ValidationSeverity.Info,
                Message = $"Field '{field.FieldName}' has low confidence ({field.Confidence:P0}). Please verify."
            });
        }
    }
}
