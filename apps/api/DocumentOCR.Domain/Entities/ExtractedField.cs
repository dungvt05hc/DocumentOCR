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

    public bool IsRequired { get; set; }
    public bool IsEditedByUser { get; set; }
    public DateTime? EditedAt { get; set; }

    public Document Document { get; set; } = null!;
}
