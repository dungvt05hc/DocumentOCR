namespace DocumentOCR.Application.Models;

/// <summary>
/// A structured key-value field candidate extracted by the OCR provider.
/// Replaces the earlier <c>OcrFieldResult</c> with richer spatial metadata.
/// </summary>
public sealed class OcrFieldCandidate
{
    /// <summary>Canonical field key as recognized by the provider (e.g. "VendorName").</summary>
    public string FieldKey { get; init; } = string.Empty;

    /// <summary>Field content as a string.</summary>
    public string? Value { get; init; }

    /// <summary>Provider confidence for this field (0–1).</summary>
    public double? Confidence { get; init; }

    /// <summary>1-based page number where the field was found.</summary>
    public int? PageNumber { get; init; }

    public BoundingBox? BoundingBox { get; init; }

    /// <summary>The exact key returned by the provider before any normalization.</summary>
    public string? RawProviderKey { get; init; }
}
