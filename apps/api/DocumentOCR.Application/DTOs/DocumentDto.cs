using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }
    public Guid? ClientProfileId { get; set; }
    public string? ClientProfileName { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public DocumentStatus Status { get; set; }
    public DocumentType DocumentType { get; set; }
    public DocumentDirection Direction { get; set; }
    public string? ErrorMessage { get; set; }
    public int WarningCount { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
