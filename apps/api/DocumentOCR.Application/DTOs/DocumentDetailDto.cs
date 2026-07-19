using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.DTOs;

public class DocumentDetailDto : DocumentDto
{
    public List<ExtractedFieldDto> Fields { get; set; } = new();
    public List<ValidationWarningDto> Warnings { get; set; } = new();
    public OcrProviderLogDto? OcrLog { get; set; }
}

public class ExtractedFieldDto
{
    public Guid Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }
    public double? Confidence { get; set; }
    public int? PageNumber { get; set; }
    public string? ExtractionMethod { get; set; }
    public string? SourceText { get; set; }
    public string? SourceType { get; set; }
    public string? ProviderFieldName { get; set; }
    public bool IsRequired { get; set; }
    public bool IsEditedByUser { get; set; }
    public DateTime? EditedAt { get; set; }
}

public class ValidationWarningDto
{
    public Guid Id { get; set; }
    public string? FieldName { get; set; }
    public string? WarningCode { get; set; }
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class OcrProviderLogDto
{
    public string ProviderName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public double ProcessingTimeMs { get; set; }
    public decimal EstimatedCost { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponsePath { get; set; }
    public string? NormalizedResultPath { get; set; }
}
