using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Domain.Entities;

public class ValidationWarning : BaseEntity
{
    public Guid DocumentId { get; set; }

    public string? FieldName { get; set; }
    public string? WarningCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    public Document Document { get; set; } = null!;
}
