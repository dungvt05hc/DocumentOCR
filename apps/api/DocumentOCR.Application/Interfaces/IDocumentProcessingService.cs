namespace DocumentOCR.Application.Interfaces;

/// <summary>Orchestrates the full OCR pipeline for a single document.</summary>
public interface IDocumentProcessingService
{
    Task ProcessAsync(Guid documentId, CancellationToken ct = default);
}
