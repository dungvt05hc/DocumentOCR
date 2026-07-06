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
    public bool IsRequired { get; set; }
    public bool IsEditedByUser { get; set; }
    public DateTime? EditedAt { get; set; }

    public Document Document { get; set; } = null!;
}
