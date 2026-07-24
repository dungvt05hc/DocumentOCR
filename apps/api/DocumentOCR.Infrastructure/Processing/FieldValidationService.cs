using System.Globalization;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Processing;

public partial class FieldValidationService : IFieldValidationService
{
    private const double LowConfidenceThreshold = 0.75;
    private const decimal AbsoluteRoundingTolerance = 1m;
    private const decimal RelativeRoundingTolerancePercent = 0.005m;

    private readonly IDocumentProfileCatalog _profileCatalog;

    public FieldValidationService(IDocumentProfileCatalog profileCatalog)
    {
        _profileCatalog = profileCatalog;
    }

    public IReadOnlyList<ValidationWarning> Validate(Guid documentId, IEnumerable<ExtractedField> fields)
    {
        var fieldList = fields.ToList();
        var fieldsByName = fieldList
            .GroupBy(f => f.FieldName)
            .ToDictionary(g => g.Key, g => g.First());

        var warnings = new List<ValidationWarning>();
        var category = GetDocumentCategory(fieldsByName);
        var profile = _profileCatalog.GetProfile(category);

        ValidateRequiredFields(documentId, fieldsByName, profile, warnings);
        ValidateTaxCode(documentId, fieldsByName, warnings);
        ValidateInvoiceDate(documentId, fieldsByName, warnings);
        ValidateTotalAmount(documentId, fieldsByName, warnings);
        ValidateAmountConsistency(documentId, fieldsByName, warnings);
        ValidateLowConfidence(documentId, fieldList, warnings);

        return warnings;
    }

    /// <summary>
    /// Resolves the review-facing category from the same "DocumentType" pseudo-field the legacy
    /// pipeline already produces (defaulting to <see cref="DocumentType.Invoice"/> when absent or
    /// unparseable — matching <c>DocumentProcessingService</c>'s existing default exactly), then
    /// delegates to the catalog for the richer 8-value resolution.
    /// </summary>
    private DocumentCategory GetDocumentCategory(IReadOnlyDictionary<string, ExtractedField> fields)
    {
        var rawValue = fields.TryGetValue(nameof(FieldName.DocumentType), out var documentTypeField)
            ? FieldValue(documentTypeField)
            : null;

        var fallbackDocumentType = Enum.TryParse<DocumentType>(rawValue, ignoreCase: true, out var parsed)
            ? parsed
            : DocumentType.Invoice;

        return _profileCatalog.ResolveCategory(rawValue, fallbackDocumentType);
    }

    private static void ValidateRequiredFields(
        Guid documentId,
        IReadOnlyDictionary<string, ExtractedField> fields,
        DocumentProfile profile,
        List<ValidationWarning> warnings)
    {
        foreach (var field in profile.Sections.SelectMany(s => s.Fields).Where(f => f.IsRequired))
        {
            if (HasValue(fields, field.FieldKey)) continue;
            if (field.AliasFieldNames.Any(alias => HasValue(fields, alias))) continue;

            // Key the warning by the legacy alias when one exists (e.g. "SupplierTaxCode" rather
            // than the profile's "SellerTaxCode") so it lines up with the literal ExtractedField
            // name a client would look for — falls back to the profile's own key for fields with
            // no legacy equivalent (e.g. "BuyerName").
            var warningKey = field.AliasFieldNames.Count > 0 ? field.AliasFieldNames[0] : field.FieldKey;

            AddWarning(
                warnings,
                documentId,
                warningKey,
                "REQUIRED_FIELD_MISSING",
                field.MissingSeverity,
                $"Required field '{warningKey}' is missing or empty.");
        }
    }

    private static void ValidateTaxCode(
        Guid documentId,
        IReadOnlyDictionary<string, ExtractedField> fields,
        List<ValidationWarning> warnings)
    {
        if (!fields.TryGetValue(nameof(FieldName.SupplierTaxCode), out var taxCode)
            || string.IsNullOrWhiteSpace(FieldValue(taxCode)))
        {
            return;
        }

        var value = FieldValue(taxCode)!;

        if (!value.All(char.IsDigit))
        {
            AddWarning(
                warnings,
                documentId,
                nameof(FieldName.SupplierTaxCode),
                "INVALID_TAX_CODE_FORMAT",
                ValidationSeverity.Warning,
                $"Tax code '{value}' contains non-digit characters after normalization.");
            return;
        }

        if (value.Length is not (10 or 13))
        {
            AddWarning(
                warnings,
                documentId,
                nameof(FieldName.SupplierTaxCode),
                "INVALID_TAX_CODE_LENGTH",
                ValidationSeverity.Warning,
                $"Tax code '{value}' has {value.Length} digits; expected 10 or 13 for a Vietnamese tax code.");
        }
    }

    private static void ValidateInvoiceDate(
        Guid documentId,
        IReadOnlyDictionary<string, ExtractedField> fields,
        List<ValidationWarning> warnings)
    {
        if (!fields.TryGetValue(nameof(FieldName.InvoiceDate), out var invoiceDate)
            || string.IsNullOrWhiteSpace(FieldValue(invoiceDate)))
        {
            return;
        }

        var value = FieldValue(invoiceDate)!;
        if (!TryParseDate(value, out var parsedDate))
        {
            AddWarning(
                warnings,
                documentId,
                nameof(FieldName.InvoiceDate),
                "INVALID_INVOICE_DATE",
                ValidationSeverity.Warning,
                $"InvoiceDate '{value}' must parse to a valid date.");
            return;
        }

        var maxAllowedDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(30));
        if (parsedDate > maxAllowedDate)
        {
            AddWarning(
                warnings,
                documentId,
                nameof(FieldName.InvoiceDate),
                "INVOICE_DATE_TOO_FAR_IN_FUTURE",
                ValidationSeverity.Warning,
                $"InvoiceDate '{value}' is more than 30 days in the future.");
        }
    }

    private static void ValidateTotalAmount(
        Guid documentId,
        IReadOnlyDictionary<string, ExtractedField> fields,
        List<ValidationWarning> warnings)
    {
        if (!fields.TryGetValue(nameof(FieldName.TotalAmount), out var totalField)
            || string.IsNullOrWhiteSpace(FieldValue(totalField)))
        {
            return;
        }

        if (!TryParseMoney(totalField, out var total) || total <= 0)
        {
            AddWarning(
                warnings,
                documentId,
                nameof(FieldName.TotalAmount),
                "INVALID_TOTAL_AMOUNT",
                ValidationSeverity.Error,
                $"TotalAmount '{FieldValue(totalField)}' must be a positive number.");
        }
    }

    private static void ValidateAmountConsistency(
        Guid documentId,
        IReadOnlyDictionary<string, ExtractedField> fields,
        List<ValidationWarning> warnings)
    {
        if (!fields.TryGetValue(nameof(FieldName.SubtotalAmount), out var subtotalField)) return;
        if (!fields.TryGetValue(nameof(FieldName.VatAmount), out var vatField)) return;
        if (!fields.TryGetValue(nameof(FieldName.TotalAmount), out var totalField)) return;

        if (!TryParseMoney(subtotalField, out var subtotal)) return;
        if (!TryParseMoney(vatField, out var vat)) return;
        if (!TryParseMoney(totalField, out var total)) return;

        var expected = subtotal + vat;
        var tolerance = Math.Max(AbsoluteRoundingTolerance, Math.Abs(total) * RelativeRoundingTolerancePercent);

        if (Math.Abs(expected - total) > tolerance)
        {
            AddWarning(
                warnings,
                documentId,
                nameof(FieldName.TotalAmount),
                "AMOUNT_CONSISTENCY_MISMATCH",
                ValidationSeverity.Warning,
                $"SubtotalAmount ({subtotal}) + VatAmount ({vat}) = {expected}, but TotalAmount is {total}. Difference exceeds the allowed rounding tolerance.");
        }
    }

    private static void ValidateLowConfidence(
        Guid documentId,
        List<ExtractedField> fields,
        List<ValidationWarning> warnings)
    {
        foreach (var field in fields.Where(f => f.Confidence.HasValue && f.Confidence < LowConfidenceThreshold))
        {
            AddWarning(
                warnings,
                documentId,
                field.FieldName,
                "LOW_CONFIDENCE",
                ValidationSeverity.Info,
                $"Field '{field.FieldName}' has low confidence ({field.Confidence:P0}). Please verify.");
        }
    }

    private static bool HasValue(IReadOnlyDictionary<string, ExtractedField> fields, string fieldName)
    {
        return fields.TryGetValue(fieldName, out var field) && !string.IsNullOrWhiteSpace(FieldValue(field));
    }

    private static string? FieldValue(ExtractedField field)
    {
        return !string.IsNullOrWhiteSpace(field.NormalizedValue)
            ? field.NormalizedValue
            : field.RawValue;
    }

    private static bool TryParseDate(string value, out DateOnly date)
    {
        var match = DatePattern().Match(value);
        var candidate = match.Success ? match.Value : value.Trim();

        string[] formats =
        [
            "dd/MM/yyyy", "d/M/yyyy",
            "dd-MM-yyyy", "d-M-yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "dd/MM/yy", "d/M/yy"
        ];

        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }

        date = default;
        return false;
    }

    private static bool TryParseMoney(ExtractedField field, out decimal value)
    {
        return TryParseMoney(FieldValue(field), out value)
               || TryParseMoney(field.RawValue, out value);
    }

    private static bool TryParseMoney(string? rawValue, out decimal value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(rawValue)) return false;

        var match = MoneyPattern().Matches(rawValue)
            .Cast<Match>()
            .LastOrDefault(m => m.Success && m.Value.Any(char.IsDigit));

        if (match is null) return false;

        var cleaned = MoneyCleanupPattern().Replace(match.Value, " ").Trim();
        cleaned = WhitespacePattern().Replace(cleaned, " ");

        if (EuropeanMoneyPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(
                cleaned.Replace(".", "").Replace(",", "."),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out value);
        }

        if (UsMoneyPattern().IsMatch(cleaned))
        {
            return decimal.TryParse(
                cleaned.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out value);
        }

        var plain = cleaned.Replace(".", "").Replace(",", "").Replace(" ", "");
        return decimal.TryParse(plain, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static void AddWarning(
        List<ValidationWarning> warnings,
        Guid documentId,
        string? fieldName,
        string warningCode,
        ValidationSeverity severity,
        string message)
    {
        warnings.Add(new ValidationWarning
        {
            DocumentId = documentId,
            FieldName = fieldName,
            WarningCode = warningCode,
            Severity = severity,
            Message = message
        });
    }

    [GeneratedRegex(@"\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}[-/]\d{1,2}[-/]\d{1,2}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đ)?\s*-?\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"[^\d.,\s-]", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyCleanupPattern();

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex EuropeanMoneyPattern();

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex UsMoneyPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
