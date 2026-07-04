using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Normalizes raw field values into clean, consistent formats.
/// Handles Vietnamese currency (e.g. "1.234.567 VND"), dates, and tax codes.
/// </summary>
public interface IFieldNormalizationService
{
    /// <summary>Normalizes a Vietnamese/international currency string. Returns null on failure.</summary>
    decimal? NormalizeCurrency(string? rawValue);

    /// <summary>Parses a date string in common Vietnamese and ISO formats. Returns null on failure.</summary>
    DateOnly? NormalizeDate(string? rawValue);

    /// <summary>Strips all non-digit characters from a tax code.</summary>
    string? NormalizeTaxCode(string? rawValue);

    /// <summary>Normalizes Vietnamese currency markers such as VND, VND with diacritics, and the dong symbol.</summary>
    string? NormalizeCurrencyCode(string? rawValue);

    /// <summary>Applies normalization to all fields of a document in place.</summary>
    void NormalizeFields(IEnumerable<ExtractedField> fields);
}
