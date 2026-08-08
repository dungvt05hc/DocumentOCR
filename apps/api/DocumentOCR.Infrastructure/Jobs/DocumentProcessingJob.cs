using DocumentOCR.Application.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace DocumentOCR.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that invokes the document processing pipeline.
/// Hangfire handles retries automatically on failure (3 attempts by default).
/// </summary>
public class DocumentProcessingJob
{
    private readonly IDocumentProcessingService _processingService;
    private readonly IClientAutoSuggestService _clientAutoSuggest;
    private readonly ILogger<DocumentProcessingJob> _logger;

    public DocumentProcessingJob(
        IDocumentProcessingService processingService,
        IClientAutoSuggestService clientAutoSuggest,
        ILogger<DocumentProcessingJob> logger)
    {
        _processingService = processingService;
        _clientAutoSuggest = clientAutoSuggest;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ProcessDocumentAsync(Guid documentId)
    {
        _logger.LogInformation("Background job starting for document {Id}", documentId);
        await _processingService.ProcessAsync(documentId);

        // Runs after the pipeline has committed extracted fields — a separate, additive step
        // that never alters extraction/normalization/validation. See IClientAutoSuggestService.
        var assigned = await _clientAutoSuggest.TrySuggestAndAssignAsync(documentId);
        if (assigned)
        {
            _logger.LogInformation("Auto-assigned a client profile to document {Id}", documentId);
        }

        // Direction depends on whichever client ended up assigned above (auto or manual from an
        // earlier run), so it always runs after — never before — the auto-assign step.
        var direction = await _clientAutoSuggest.InferDirectionAsync(documentId);
        _logger.LogInformation("Inferred direction {Direction} for document {Id}", direction, documentId);

        _logger.LogInformation("Background job completed for document {Id}", documentId);
    }
}
