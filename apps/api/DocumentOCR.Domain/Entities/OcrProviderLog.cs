using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class OcrProviderLog : BaseEntity
{
    public Guid DocumentId { get; set; }

    /// <summary>Human-readable provider name (e.g. "AzureDocumentIntelligence").</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Provider-specific model identifier used for this run (e.g. "prebuilt-invoice", "Fake").</summary>
    public string? ModelId { get; set; }
    public int PageCount { get; set; }
    public double ProcessingTimeMs { get; set; }
    public decimal EstimatedCost { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Raw provider response JSON, kept for debugging only — never used for business logic.</summary>
    public string? RawResponseJson { get; set; }

    public Document Document { get; set; } = null!;
}
