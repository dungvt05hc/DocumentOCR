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
    private readonly ILogger<DocumentProcessingJob> _logger;

    public DocumentProcessingJob(
        IDocumentProcessingService processingService,
        ILogger<DocumentProcessingJob> logger)
    {
        _processingService = processingService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public async Task ProcessDocumentAsync(Guid documentId)
    {
        _logger.LogInformation("Background job starting for document {Id}", documentId);
        await _processingService.ProcessAsync(documentId);
        _logger.LogInformation("Background job completed for document {Id}", documentId);
    }
}
