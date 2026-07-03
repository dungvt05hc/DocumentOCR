using System.Globalization;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Normalizes field values for Vietnamese invoices and receipts.
///
/// Currency formats handled:
///   "1.234.567"        → 1234567   (dots as thousands separators, Vietnamese style)
///   "1,234,567"        → 1234567   (commas as thousands separators)
///   "1.234.567 VND"    → 1234567
///   "1 234 567"        → 1234567   (spaces as thousands separators)
///   "1.234.567,50"     → 1234567.5 (European: dot=thousands, comma=decimal)
///   "1,234,567.50"     → 1234567.5 (US: comma=thousands, dot=decimal)
///   "1234567"          → 1234567
/// </summary>
public partial class FieldNormalizationService : IFieldNormalizationService
{
    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd.MM.yyyy", "d.M.yyyy",
        "yyyy-MM-dd",          // ISO 8601
        "dd/MM/yy", "d/M/yy",
        "yyyy/MM/dd"
    ];

    public decimal? NormalizeCurrency(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        // Strip currency symbols and whitespace, convert to uppercase for easy detection
        var cleaned = CurrencySymbolPattern().Replace(rawValue, " ").Trim();

        if (string.IsNullOrWhiteSpace(cleaned)) return null;

        // Case 1: European style — "1.234.567,50" (dot=thousands, comma=decimal)
        // Identified when last separator is a comma AND dots appear earlier
        if (EuropeanCurrencyPattern().IsMatch(cleaned))
        {
            var europeanStr = cleaned.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(europeanStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var europeanVal))
                return europeanVal;
        }

        // Case 2: US style — "1,234,567.50" (comma=thousands, dot=decimal)
        if (UsCurrencyPattern().IsMatch(cleaned))
        {
            var usStr = cleaned.Replace(",", "");
            if (decimal.TryParse(usStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var usVal))
                return usVal;
        }

        // Case 3: Vietnamese/plain — "1.234.567" or "1,234,567" (all dots or all commas = thousands)
        // Remove all dots, commas, and spaces (they are all thousands separators)
        var plain = cleaned.Replace(".", "").Replace(",", "").Replace(" ", "");
        if (decimal.TryParse(plain, NumberStyles.Any, CultureInfo.InvariantCulture, out var plainVal))
            return plainVal;

        return null;
    }

    public DateOnly? NormalizeDate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var trimmed = rawValue.Trim();

        foreach (var fmt in DateFormats)
        {
            if (DateOnly.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                return date;
        }

        // Fallback: try general DateTime parsing
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }

    public string? NormalizeTaxCode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        return NonDigitPattern().Replace(rawValue, "");
    }

    public void NormalizeFields(IEnumerable<ExtractedField> fields)
    {
        foreach (var field in fields)
        {
            field.NormalizedValue = field.FieldName switch
            {
                nameof(FieldName.TotalAmount) or nameof(FieldName.SubtotalAmount) or nameof(FieldName.VatAmount)
                    => NormalizeCurrency(field.RawValue)?.ToString(CultureInfo.InvariantCulture),

                nameof(FieldName.InvoiceDate)
                    => NormalizeDate(field.RawValue)?.ToString("yyyy-MM-dd"),

                nameof(FieldName.SupplierTaxCode)
                    => NormalizeTaxCode(field.RawValue),

                _ => field.RawValue?.Trim()
            };
        }
    }

    // ── Compiled Regex patterns ──────────────────────────────────────────────────

    /// Matches "1.234,50" — European format: dots for thousands, single trailing comma+decimal
    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex EuropeanCurrencyPattern();

    /// Matches "1,234.50" — US format: commas for thousands, single trailing dot+decimal
    [GeneratedRegex(@"^\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex UsCurrencyPattern();

    /// Strips currency symbols, codes, and extra whitespace
    [GeneratedRegex(@"[^\d.,\s]", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencySymbolPattern();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitPattern();
}
