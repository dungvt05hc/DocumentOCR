using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class DocumentPage : BaseEntity
{
    public Guid DocumentId { get; set; }
    public int PageNumber { get; set; }

    /// <summary>Raw text extracted from this page by the OCR provider.</summary>
    public string RawText { get; set; } = string.Empty;

    public Document Document { get; set; } = null!;
}
