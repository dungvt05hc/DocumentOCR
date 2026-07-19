using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentOCR.Infrastructure.Processing;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Compares extracted field values against a <see cref="GroundTruthRow"/> using the matching
/// rules from the ground-truth benchmarking spec: normalized case/whitespace/punctuation text
/// comparison, digits-only tax codes, numeric money, and ISO dates. A <see langword="null"/>
/// (rather than <see langword="true"/>/<see langword="false"/>) result means "not evaluated" —
/// the ground truth left that field blank.
/// </summary>
public static partial class GroundTruthComparer
{
    private static readonly FieldNormalizationService Normalization = new();

    public static bool? MatchText(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return null;
        return string.Equals(NormalizeText(expected), NormalizeText(actual), StringComparison.Ordinal);
    }

    public static bool? MatchTaxCode(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return null;

        var expectedDigits = Normalization.NormalizeTaxCode(expected);
        var actualDigits = Normalization.NormalizeTaxCode(actual);
        return expectedDigits is not null && expectedDigits == actualDigits;
    }

    public static bool? MatchMoney(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return null;

        var expectedValue = Normalization.NormalizeCurrency(expected);
        var actualValue = Normalization.NormalizeCurrency(actual);
        return expectedValue.HasValue && actualValue.HasValue && expectedValue == actualValue;
    }

    public static bool? MatchDate(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return null;

        var expectedDate = Normalization.NormalizeDate(expected);
        var actualDate = Normalization.NormalizeDate(actual);
        return expectedDate.HasValue && actualDate.HasValue && expectedDate == actualDate;
    }

    public static bool? MatchCurrency(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return null;
        return string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Percentage of the given match results that were evaluated (non-null) and matched.
    /// Returns <see langword="null"/> when none of the fields had a ground truth value to
    /// compare against, so the CSV cell is left blank rather than showing a misleading 0%/100%.
    /// </summary>
    public static double? CalculateFieldAccuracyPercent(params bool?[] matches)
    {
        var evaluated = matches.Where(m => m.HasValue).ToList();
        if (evaluated.Count == 0) return null;

        var matchedCount = evaluated.Count(m => m!.Value);
        return Math.Round(matchedCount / (double)evaluated.Count * 100, 2);
    }

    // Ground-truth CSVs are frequently typed without Vietnamese diacritics (or inconsistently
    // with them) even when the OCR'd document itself has them — e.g. "CONG TY TNHH ABC" as
    // ground truth for an extracted "CÔNG TY TNHH ABC". Stripping diacritics before comparison
    // avoids under-reporting accuracy for otherwise-correct extractions.
    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var lowered = RemoveDiacritics(value.Trim()).ToLowerInvariant();
        var withoutPunctuation = CommonPunctuationPattern().Replace(lowered, " ");
        return WhitespacePattern().Replace(withoutPunctuation, " ").Trim();
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c switch
                {
                    'Đ' => 'D',
                    'đ' => 'd',
                    _ => c
                });
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[.,;:'""`\-_(){}\[\]/\\!?]")]
    private static partial Regex CommonPunctuationPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
