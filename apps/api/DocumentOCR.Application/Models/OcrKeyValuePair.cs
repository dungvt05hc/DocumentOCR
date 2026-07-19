namespace DocumentOCR.Application.Models;

/// <summary>
/// A generic key-value pair detected by the OCR provider's "keyValuePairs" add-on feature.
/// Distinct from <see cref="OcrFieldCandidate"/>, which represents provider-specific prebuilt
/// model fields (e.g. prebuilt-invoice's VendorName) rather than free-form layout key-value pairs.
/// </summary>
public sealed class OcrKeyValuePair
{
    public string KeyText { get; init; } = string.Empty;
    public string? ValueText { get; init; }
    public double? Confidence { get; init; }

    /// <summary>1-based page number where the key was found, when available.</summary>
    public int? PageNumber { get; init; }

    public BoundingBox? KeyBoundingBox { get; init; }
    public BoundingBox? ValueBoundingBox { get; init; }
}
