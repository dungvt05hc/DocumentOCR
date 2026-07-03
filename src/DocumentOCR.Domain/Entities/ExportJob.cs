using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Domain.Entities;

public class ExportJob : BaseEntity
{
    public string StoredFilePath { get; set; } = string.Empty;
    public ExportJobStatus Status { get; set; } = ExportJobStatus.Pending;
    public string? ErrorMessage { get; set; }

    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
