using DocumentOCR.Application.Models;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Vendor-neutral OCR provider abstraction (Ports &amp; Adapters).
/// Implementations must not leak vendor SDK types outside the Infrastructure layer.
/// </summary>
public interface IDocumentOcrProvider
{
    /// <summary>Human-readable provider identifier (e.g. "AzureDocumentIntelligence", "Fake").</summary>
    string ProviderName { get; }

    /// <summary>Analyse a document and return a provider-neutral normalized OCR document.</summary>
    Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default);
}
