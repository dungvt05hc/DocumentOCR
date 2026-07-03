using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Orchestrates the full OCR pipeline for a single document:
///   1. Load document + open file stream
///   2. Call OCR provider
///   3. Persist OCR provider log
///   4. Extract fields
///   5. Normalise field values
///   6. Validate fields + produce warnings
///   7. Update document status
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
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            _logger.LogWarning("Document {Id} not found — skipping processing.", documentId);
            return;
        }

        document.Status = DocumentStatus.Processing;
        document.ProcessingStartedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            // ── Step 1: OCR ──────────────────────────────────────────────────────
            await using var fileStream = await _storage.GetStreamAsync(document.StoredFilePath, ct);
            var ocrInput = new DocumentInput
            {
                Content = fileStream,
                FileName = document.OriginalFileName,
                ContentType = document.ContentType,
                FileSizeBytes = document.FileSizeBytes
            };
            var ocrResult = await _ocrProvider.AnalyzeAsync(ocrInput, ct);

            // ── Step 2: Persist OCR log ────────────────────────────────────
            var ocrLog = new OcrProviderLog
            {
                DocumentId = documentId,
                ProviderName = _ocrProvider.ProviderName,
                PageCount = ocrResult.PageCount,
                ProcessingTimeMs = ocrResult.ProcessingTimeMs,
                EstimatedCost = ocrResult.EstimatedCost,
                Success = ocrResult.Success,
                ErrorMessage = ocrResult.ErrorMessage
            };
            _db.OcrProviderLogs.Add(ocrLog);

            if (!ocrResult.Success)
            {
                document.Status = DocumentStatus.Failed;
                document.ErrorMessage = ocrResult.ErrorMessage;
                document.ProcessingCompletedAt = DateTime.UtcNow;
                document.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return;
            }

            // ── Step 3: Persist page text ────────────────────────────────────────
            foreach (var page in ocrResult.Pages)
            {
                _db.DocumentPages.Add(new DocumentPage
                {
                    DocumentId = documentId,
                    PageNumber = page.PageNumber,
                    RawText = page.FullText
                });
            }

            // ── Step 4: Extract fields ───────────────────────────────────────────
            // Remove any stale fields from a previous attempt
            var staleFields = document.Fields.ToList();
            foreach (var f in staleFields) _db.ExtractedFields.Remove(f);

            var staleWarnings = document.ValidationWarnings.ToList();
            foreach (var w in staleWarnings) _db.ValidationWarnings.Remove(w);

            var extractedFields = _extraction.Extract(documentId, ocrResult);

            // ── Step 5: Normalise ────────────────────────────────────────────────
            _normalization.NormalizeFields(extractedFields);

            foreach (var field in extractedFields)
                _db.ExtractedFields.Add(field);

            // ── Step 6: Validate ─────────────────────────────────────────────────
            var warnings = _validation.Validate(documentId, extractedFields);
            foreach (var warning in warnings)
                _db.ValidationWarnings.Add(warning);

            // ── Step 7: Finalise ─────────────────────────────────────────────────
            document.Status = DocumentStatus.Processed;
            document.PageCount = ocrResult.PageCount;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _usage.TrackAsync(
                _ocrProvider.ProviderName,
                ocrResult.PageCount,
                (long)ocrResult.ProcessingTimeMs,
                ocrResult.EstimatedCost,
                ct);

            _logger.LogInformation("Document {Id} processed successfully. {FieldCount} fields, {WarningCount} warnings.",
                documentId, extractedFields.Count, warnings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for document {Id}", documentId);

            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            try { await _db.SaveChangesAsync(ct); }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save failure status for document {Id}", documentId);
            }
        }
    }
}
