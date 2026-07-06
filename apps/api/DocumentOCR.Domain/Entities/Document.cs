using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Domain.Entities;

public class Document : BaseEntity
{
    public Guid OrganizationId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public DocumentType DocumentType { get; set; } = DocumentType.Unknown;

    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public Organization Organization { get; set; } = null!;
    public ICollection<DocumentPage> Pages { get; set; } = new List<DocumentPage>();
    public ICollection<ExtractedField> Fields { get; set; } = new List<ExtractedField>();
    public ICollection<ValidationWarning> ValidationWarnings { get; set; } = new List<ValidationWarning>();
    public ICollection<OcrProviderLog> OcrProviderLogs { get; set; } = new List<OcrProviderLog>();
}
