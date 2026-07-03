using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class UsageLog : BaseEntity
{
    public string ProviderName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public long ProcessingDurationMs { get; set; }
    public decimal EstimatedCost { get; set; }
}
