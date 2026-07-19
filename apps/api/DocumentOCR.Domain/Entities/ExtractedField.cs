using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class ExtractedField : BaseEntity
{
    public Guid DocumentId { get; set; }

    public string FieldName { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }

    /// <summary>Confidence score 0–1 as returned by the OCR provider.</summary>
    public double? Confidence { get; set; }

    public int? PageNumber { get; set; }
    public string? BoundingBoxJson { get; set; }

    /// <summary>Which extraction strategy produced the winning value (e.g. "StructuredField", "LineKeyword", "FullTextRegex").</summary>
    public string? ExtractionMethod { get; set; }

    /// <summary>The raw line/text the winning candidate was derived from, for debugging and audit.</summary>
    public string? SourceText { get; set; }

    /// <summary>Coarse category of <see cref="ExtractionMethod"/> (e.g. "StructuredField", "KeyValuePair", "Line", "Table", "Regex", "Heuristic").</summary>
    public string? SourceType { get; set; }

    /// <summary>The provider's own field/key name before mapping to a canonical <see cref="FieldName"/> (e.g. Azure's "VendorName" or a layout key-value pair's key text).</summary>
    public string? ProviderFieldName { get; set; }

    public bool IsRequired { get; set; }
    public bool IsEditedByUser { get; set; }
    public DateTime? EditedAt { get; set; }

    public Document Document { get; set; } = null!;
}
