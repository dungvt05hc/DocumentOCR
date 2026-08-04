using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.DTOs;

public class AccountingReportExportRequest
{
    public AccountingReportType Type { get; set; }
    public Guid ClientProfileId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}
