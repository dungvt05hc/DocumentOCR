using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Orchestrates the full OCR pipeline for a single document.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorageService _storage;
    private readonly IDocumentOcrProvider _ocrProvider;
    private readonly IFieldExtractionService _extraction;
    private readonly IFieldNormalizationService _normalization;
    private readonly IFieldValidationService _validation;
    private readonly IUsageTrackingService _usage;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        IApplicationDbContext db,
        IDocumentStorageService storage,
        IDocumentOcrProvider ocrProvider,
        IFieldExtractionService extraction,
        IFieldNormalizationService normalization,
        IFieldValidationService validation,
        IUsageTrackingService usage,
        ILogger<DocumentProcessingService> logger)
    {
        _db = db;
        _storage = storage;
        _ocrProvider = ocrProvider;
        _extraction = extraction;
        _normalization = normalization;
        _validation = validation;
        _usage = usage;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Pages)
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLog)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            _logger.LogWarning("Document {DocumentId} not found. Skipping processing.", documentId);
            return;
        }

        _logger.LogInformation(
            "Starting document processing for {DocumentId} ({FileName}, {ContentType}, {FileSizeBytes} bytes)",
            documentId,
            document.OriginalFileName,
            document.ContentType,
            document.FileSizeBytes);

        document.Status = DocumentStatus.Processing;
        document.ProcessingStartedAt = DateTime.UtcNow;
        document.ProcessingCompletedAt = null;
        document.ErrorMessage = null;
        document.UpdatedAt = DateTime.UtcNow;

        RemoveStaleProcessingArtifacts(document);
        await _db.SaveChangesAsync(ct);

        try
        {
            _logger.LogInformation(
                "Loading stored file for document {DocumentId} from {StoredFilePath}",
                documentId,
                document.StoredFilePath);

            await using var fileStream = await _storage.GetStreamAsync(document.StoredFilePath, ct);
            var ocrInput = new DocumentInput
            {
                Content = fileStream,
                FileName = document.OriginalFileName,
                ContentType = document.ContentType,
                FileSizeBytes = document.FileSizeBytes
            };

            _logger.LogInformation(
                "Calling OCR provider {ProviderName} for document {DocumentId}",
                _ocrProvider.ProviderName,
                documentId);

            var ocrResult = await _ocrProvider.AnalyzeAsync(ocrInput, ct);

            _logger.LogInformation(
                "OCR provider {ProviderName} completed for document {DocumentId}. Success={Success}, Pages={PageCount}, DurationMs={ProcessingTimeMs}, EstimatedCost={EstimatedCost}",
                _ocrProvider.ProviderName,
                documentId,
                ocrResult.Success,
                ocrResult.PageCount,
                ocrResult.ProcessingTimeMs,
                ocrResult.EstimatedCost);

            _db.OcrProviderLogs.Add(new OcrProviderLog
            {
                DocumentId = documentId,
                ProviderName = _ocrProvider.ProviderName,
                PageCount = ocrResult.PageCount,
                ProcessingTimeMs = ocrResult.ProcessingTimeMs,
                EstimatedCost = ocrResult.EstimatedCost,
                Success = ocrResult.Success,
                ErrorMessage = ocrResult.ErrorMessage
            });

            if (!ocrResult.Success)
            {
                document.Status = DocumentStatus.Failed;
                document.ErrorMessage = string.IsNullOrWhiteSpace(ocrResult.ErrorMessage)
                    ? "OCR provider failed without an error message."
                    : ocrResult.ErrorMessage;
                document.ProcessingCompletedAt = DateTime.UtcNow;
                document.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                await TrackUsageSafelyAsync(ocrResult, ct);

                _logger.LogWarning(
                    "Document {DocumentId} marked Failed because OCR provider {ProviderName} returned an unsuccessful result: {ErrorMessage}",
                    documentId,
                    _ocrProvider.ProviderName,
                    document.ErrorMessage);

                return;
            }

            foreach (var page in ocrResult.Pages)
            {
                _db.DocumentPages.Add(new DocumentPage
                {
                    DocumentId = documentId,
                    PageNumber = page.PageNumber,
                    RawText = page.FullText
                });
            }

            var extractedFields = _extraction.Extract(documentId, ocrResult);
            _logger.LogInformation(
                "Extracted {FieldCount} fields for document {DocumentId}",
                extractedFields.Count,
                documentId);

            _normalization.NormalizeFields(extractedFields);

            foreach (var field in extractedFields)
                _db.ExtractedFields.Add(field);

            var warnings = _validation.Validate(documentId, extractedFields);
            foreach (var warning in warnings)
                _db.ValidationWarnings.Add(warning);

            document.DocumentType = GetDetectedDocumentType(extractedFields);
            document.Status = DocumentStatus.Processed;
            document.PageCount = ocrResult.PageCount;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await TrackUsageSafelyAsync(ocrResult, ct);

            _logger.LogInformation(
                "Document {DocumentId} processed successfully. Status={Status}, DocumentType={DocumentType}, Pages={PageCount}, Fields={FieldCount}, Warnings={WarningCount}",
                documentId,
                document.Status,
                document.DocumentType,
                document.PageCount,
                extractedFields.Count,
                warnings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);

            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save failure status for document {DocumentId}", documentId);
            }
        }
    }

    private void RemoveStaleProcessingArtifacts(Document document)
    {
        foreach (var page in document.Pages.ToList())
            _db.DocumentPages.Remove(page);

        foreach (var field in document.Fields.ToList())
            _db.ExtractedFields.Remove(field);

        foreach (var warning in document.ValidationWarnings.ToList())
            _db.ValidationWarnings.Remove(warning);

        if (document.OcrProviderLog is not null)
            _db.OcrProviderLogs.Remove(document.OcrProviderLog);
    }

    private async Task TrackUsageSafelyAsync(OcrResult ocrResult, CancellationToken ct)
    {
        try
        {
            await _usage.TrackAsync(
                _ocrProvider.ProviderName,
                ocrResult.PageCount,
                (long)ocrResult.ProcessingTimeMs,
                ocrResult.EstimatedCost,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to track usage for OCR provider {ProviderName}. Pages={PageCount}, DurationMs={ProcessingTimeMs}, EstimatedCost={EstimatedCost}",
                _ocrProvider.ProviderName,
                ocrResult.PageCount,
                ocrResult.ProcessingTimeMs,
                ocrResult.EstimatedCost);
        }
    }

    private static DocumentType GetDetectedDocumentType(IEnumerable<ExtractedField> fields)
    {
        var documentTypeField = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.DocumentType));
        var value = documentTypeField?.NormalizedValue ?? documentTypeField?.RawValue;

        return Enum.TryParse<DocumentType>(value, ignoreCase: true, out var documentType)
            ? documentType
            : DocumentType.Unknown;
    }
}
