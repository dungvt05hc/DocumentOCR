using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Normalizes field values for Vietnamese invoices and receipts.
/// </summary>
public partial class FieldNormalizationService : IFieldNormalizationService
{
    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd.MM.yyyy", "d.M.yyyy",
        "yyyy-MM-dd",
        "dd/MM/yy", "d/M/yy",
        "yyyy/MM/dd"
    ];

    public decimal? NormalizeCurrency(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var match = MoneyValuePattern().Matches(rawValue)
            .Cast<Match>()
            .LastOrDefault(m => m.Success && m.Value.Any(char.IsDigit));

        if (match is null) return null;

        var cleaned = CurrencySymbolPattern().Replace(match.Value, " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return null;

        cleaned = WhitespacePattern().Replace(cleaned, " ");

        if (EuropeanCurrencyPattern().IsMatch(cleaned))
        {
            var european = cleaned.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(european, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        if (UsCurrencyPattern().IsMatch(cleaned))
        {
            var us = cleaned.Replace(",", "");
            if (decimal.TryParse(us, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        var plain = cleaned.Replace(".", "").Replace(",", "").Replace(" ", "");
        return decimal.TryParse(plain, NumberStyles.Any, CultureInfo.InvariantCulture, out var plainValue)
            ? plainValue
            : null;
    }

    public DateOnly? NormalizeDate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var match = DateValuePattern().Match(rawValue);
        var trimmed = match.Success ? match.Value : rawValue.Trim();

        foreach (var format in DateFormats)
        {
            if (DateOnly.TryParseExact(
                    trimmed,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return date;
            }
        }

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            ? DateOnly.FromDateTime(dateTime)
            : null;
    }

    public string? NormalizeTaxCode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var digits = NonDigitPattern().Replace(rawValue, "");
        return digits.Length == 0 ? null : digits;
    }

    public string? NormalizeCurrencyCode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var normalized = RemoveDiacritics(rawValue).ToUpperInvariant();
        return normalized.Contains("VND", StringComparison.Ordinal)
               || rawValue.Contains("VNĐ", StringComparison.OrdinalIgnoreCase)
               || rawValue.Contains('₫')
               || rawValue.Contains('đ')
               || rawValue.Contains('Đ')
               || normalized.Contains("DONG", StringComparison.Ordinal)
            ? "VND"
            : null;
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

                nameof(FieldName.Currency)
                    => NormalizeCurrencyCode(field.RawValue),

                _ => field.RawValue?.Trim()
            };
        }
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyValuePattern();

    [GeneratedRegex(@"\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}-\d{1,2}-\d{1,2}")]
    private static partial Regex DateValuePattern();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+,\d+$")]
    private static partial Regex EuropeanCurrencyPattern();

    [GeneratedRegex(@"^\d{1,3}(,\d{3})+\.\d+$")]
    private static partial Regex UsCurrencyPattern();

    [GeneratedRegex(@"[^\d.,\s]", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencySymbolPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitPattern();
}
