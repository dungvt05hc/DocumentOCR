using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.DTOs;

/// <summary>
/// Dynamic, document-category-driven review response — sections/fields come from the resolved
/// <c>DocumentProfile</c>, not a hardcoded invoice shape. Additive alongside <see cref="DocumentDetailDto"/>,
/// which keeps serving its existing consumers unchanged.
/// </summary>
public class DocumentReviewResponse
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>Needed by the frontend preview pane (PDF vs. image rendering) — not in the original spec, added deliberately.</summary>
    public string ContentType { get; set; } = string.Empty;

    public DocumentStatus Status { get; set; }
    public DocumentCategory DocumentCategory { get; set; }

    /// <summary>The underlying legacy-pipeline <c>DocumentType</c>, for visibility even after collapsing to the coarser category.</summary>
    public string? DocumentSubType { get; set; }

    public string? ProviderName { get; set; }
    public string? ModelId { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public double? OverallConfidence { get; set; }

    public List<ReviewSection> Sections { get; set; } = new();
    public List<ReviewWarningDto> Warnings { get; set; } = new();

    /// <summary>Reserved for a future OCR debug viewer surface — always null for now.</summary>
    public string? DebugSummary { get; set; }
}

public class ReviewSection
{
    public string SectionKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public List<ReviewField> Fields { get; set; } = new();
}

public class ReviewField
{
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Convenience projection: <see cref="NormalizedValue"/> if present, else <see cref="RawValue"/>.</summary>
    public string? Value { get; set; }

    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }
    public ReviewFieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsEditable { get; set; } = true;
    public bool IsMissing { get; set; }
    public double? Confidence { get; set; }
    public int DisplayOrder { get; set; }
    public string? SourceType { get; set; }
    public string? SourceText { get; set; }
    public int? SourcePageNumber { get; set; }
    public string? SourceBoundingBoxJson { get; set; }
    public string? ExtractionMethod { get; set; }
    public List<string> WarningCodes { get; set; } = new();

    /// <summary>Fixed choices for Enum/Currency fields — null means "render as free text". Always null for this MVP iteration.</summary>
    public List<string>? Options { get; set; }

    /// <summary>Preserves the existing review UI's "· edited" indicator — not in the original spec, added deliberately.</summary>
    public bool IsEditedByUser { get; set; }
    public DateTime? EditedAt { get; set; }
}

/// <summary>
/// Distinct from the legacy <see cref="ValidationWarningDto"/> (which names the property
/// <c>FieldName</c>) because the dynamic review response keys warnings by <c>FieldKey</c> instead.
/// </summary>
public class ReviewWarningDto
{
    public ValidationSeverity Severity { get; set; }
    public string? FieldKey { get; set; }
    public string? WarningCode { get; set; }
    public string Message { get; set; } = string.Empty;
}
