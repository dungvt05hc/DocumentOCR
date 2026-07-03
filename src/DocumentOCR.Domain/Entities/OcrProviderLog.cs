using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class OcrProviderLog : BaseEntity
{
    public Guid DocumentId { get; set; }

    /// <summary>Human-readable provider name (e.g. "AzureDocumentIntelligence").</summary>
    public string ProviderName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public double ProcessingTimeMs { get; set; }
    public decimal EstimatedCost { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public Document Document { get; set; } = null!;
}
