using System.Text.Json;
using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Orchestrates the full document processing pipeline: resolves every registered
/// <see cref="IDocumentExtractionStrategy"/> (XML fast path, PDF-text-layer+LLM, OCR — see
/// <c>docs/decisions.md</c>), tries them in <c>Priority</c> order, then normalizes/validates/
/// persists whichever one succeeds. Catches its own failures internally (never rethrows), so
/// <c>Status = Failed</c> set here is this system's only notion of "processing failed
/// permanently" — Hangfire's <c>[AutomaticRetry]</c> on the calling job never actually re-runs a
/// business-logic failure, only an exception this method didn't catch.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorageService _storage;
    private readonly IReadOnlyList<IDocumentExtractionStrategy> _strategies;
    private readonly IFieldNormalizationService _normalization;
    private readonly IFieldValidationService _validation;
    private readonly ICreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        IApplicationDbContext db,
        IDocumentStorageService storage,
        IEnumerable<IDocumentExtractionStrategy> strategies,
        IFieldNormalizationService normalization,
        IFieldValidationService validation,
        ICreditService creditService,
        IOptions<CreditOptions> creditOptions,
        ILogger<DocumentProcessingService> logger)
    {
        _db = db;
        _storage = storage;
        _strategies = strategies.OrderBy(s => s.Priority).ToList();
        _normalization = normalization;
        _validation = validation;
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Pages)
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.TaxBreakdowns)
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
        await SaveChangesWithDiagnosticsAsync(documentId, "ProcessAsync:InitialStatusSave", ct);

        try
        {
            _logger.LogInformation(
                "Loading stored file for document {DocumentId} from {StoredFilePath}",
                documentId,
                document.StoredFilePath);

            await using var fileStream = await _storage.GetStreamAsync(document.StoredFilePath, ct);

            var input = new DocumentInput
            {
                Content = fileStream,
                FileName = document.OriginalFileName,
                ContentType = document.ContentType,
                FileSizeBytes = document.FileSizeBytes
            };

            var result = await RunStrategiesAsync(documentId, input, ct);

            if (!result.Success)
            {
                document.Status = DocumentStatus.Failed;
                document.ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Document processing failed without an error message."
                    : result.ErrorMessage;
                document.ProcessingCompletedAt = DateTime.UtcNow;
                document.UpdatedAt = DateTime.UtcNow;

                await SaveChangesWithDiagnosticsAsync(documentId, "ProcessAsync:FailureSave", ct);

                _logger.LogWarning(
                    "Document {DocumentId} marked Failed — no extraction strategy succeeded. Last error: {ErrorMessage}",
                    documentId,
                    document.ErrorMessage);

                await RefundProcessingCreditsAsync(document, CancellationToken.None);
                return;
            }

            await PersistSuccessfulResultAsync(document, result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);

            await MarkFailedAsync(
                documentId, "Document processing failed unexpectedly. Please retry or contact support.", CancellationToken.None);

            await RefundProcessingCreditsAsync(document, CancellationToken.None);
        }
    }

    /// <summary>
    /// Tries every registered strategy in <c>Priority</c> order. A strategy that claims a document
    /// via <see cref="IDocumentExtractionStrategy.CanHandleAsync"/> but fails at
    /// <see cref="IDocumentExtractionStrategy.ExtractAsync"/> (parse error, LLM timeout, ...) falls
    /// through to the next candidate rather than failing the document immediately — see
    /// <see cref="IDocumentExtractionStrategy"/>'s remarks. Every attempt (success or failure) gets
    /// its own <see cref="OcrProviderLog"/> row, so a "LLM tried and timed out, fell back to Azure
    /// OCR, which succeeded" run is fully auditable, not just the winning attempt.
    /// </summary>
    private async Task<StructuredExtractionResult> RunStrategiesAsync(Guid documentId, DocumentInput input, CancellationToken ct)
    {
        StructuredExtractionResult? lastResult = null;

        foreach (var strategy in _strategies)
        {
            bool canHandle;
            try
            {
                canHandle = await strategy.CanHandleAsync(input, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Strategy {StrategyName} threw from CanHandleAsync for document {DocumentId}; skipping.",
                    strategy.Name, documentId);
                continue;
            }

            if (!canHandle) continue;

            input.Content.Position = 0;

            // No try/catch here, unlike CanHandleAsync above: per IDocumentExtractionStrategy's
            // contract, a strategy must catch its own expected failure modes (parse error,
            // provider timeout) and return Success = false — see PdfTextLayerLlmStrategy's Gemini
            // timeout handling. An exception escaping ExtractAsync means something genuinely
            // unexpected happened, and should propagate to ProcessAsync's outer catch (generic
            // failure message, MarkFailedAsync) exactly as before this pipeline had strategies.
            var result = await strategy.ExtractAsync(input, ct);

            _logger.LogInformation(
                "Extraction strategy {StrategyName} attempted for document {DocumentId}: Success={Success}, Provider={ProviderName}",
                strategy.Name, documentId, result.Success, result.ProviderName);

            _db.OcrProviderLogs.Add(new OcrProviderLog
            {
                DocumentId = documentId,
                ProviderName = result.ProviderName,
                ModelId = result.ModelId,
                PageCount = result.PageCount,
                ProcessingTimeMs = result.ProcessingTimeMs,
                EstimatedCost = result.EstimatedCost,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                RawResponseJson = result.RawResponseJson,
                RawResponsePath = result.RawResponsePath,
                NormalizedResultPath = result.NormalizedResultPath
            });

            lastResult = result;

            if (result.Success) return result;
        }

        return lastResult ?? new StructuredExtractionResult
        {
            Success = false,
            ErrorMessage = $"No extraction strategy could handle content type '{input.ContentType}'.",
            ProviderName = "None"
        };
    }

    private async Task PersistSuccessfulResultAsync(Document document, StructuredExtractionResult result, CancellationToken ct)
    {
        var documentId = document.Id;

        foreach (var page in result.Pages)
        {
            _db.DocumentPages.Add(new DocumentPage
            {
                DocumentId = documentId,
                PageNumber = page.PageNumber,
                RawText = page.FullText
            });
        }

        document.TablesJson = result.Tables is { Count: > 0 }
            ? JsonSerializer.Serialize(result.Tables)
            : null;

        // Safe no-op for fields a strategy already normalized itself (NormalizeFields skips any
        // field with a NormalizedValue already set) — still does real work for fields the strategy
        // left un-normalized (e.g. XML's InvoiceDate/SupplierTaxCode).
        _normalization.NormalizeFields(result.Fields);

        foreach (var field in result.Fields)
        {
            field.DocumentId = documentId;
            _db.ExtractedFields.Add(field);
        }

        foreach (var taxLine in result.TaxBreakdown)
        {
            taxLine.DocumentId = documentId;
            _db.InvoiceTaxBreakdowns.Add(taxLine);
        }

        var warnings = _validation.Validate(documentId, result.Fields);
        foreach (var warning in warnings)
            _db.ValidationWarnings.Add(warning);

        document.DocumentType = GetDetectedDocumentType(result.Fields);
        document.Status = DocumentStatus.Processed;
        document.PageCount = result.PageCount;
        document.ProcessingCompletedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        await SaveChangesWithDiagnosticsAsync(documentId, "ProcessAsync:FinalSave", ct);

        _logger.LogInformation(
            "Document {DocumentId} processed successfully via {ProviderName}. Status={Status}, DocumentType={DocumentType}, Pages={PageCount}, Fields={FieldCount}, Warnings={WarningCount}",
            documentId,
            result.ProviderName,
            document.Status,
            document.DocumentType,
            document.PageCount,
            result.Fields.Count,
            warnings.Count);

        await RefundFreePathDifferenceAsync(document, result.ProviderName, ct);
    }

    /// <summary>
    /// Records a permanent processing failure for <paramref name="documentId"/>, robust to the
    /// triggering exception having left <see cref="_db"/>'s change tracker holding invalid state
    /// (e.g. a child row that failed a unique-constraint insert) — <see cref="ChangeTracker.Clear"/>
    /// discards all of that before this targeted, minimal update, so this save can't fail for the
    /// same reason the original one did. Without this, a save failure here used to be logged and
    /// swallowed, leaving the document stuck in <see cref="DocumentStatus.Processing"/> forever —
    /// Hangfire still saw the job return normally, since <see cref="ProcessAsync"/> never rethrows.
    /// </summary>
    private async Task MarkFailedAsync(Guid documentId, string errorMessage, CancellationToken ct)
    {
        try
        {
            _db.ChangeTracker.Clear();

            var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
            if (document is null) return;

            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = errorMessage;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception saveEx)
        {
            if (saveEx is DbUpdateException dbEx)
                LogSaveChangesFailure(documentId, "MarkFailedAsync", dbEx);
            else
                _logger.LogError(saveEx, "Failed to save failure status for document {DocumentId}", documentId);
        }
    }

    /// <summary>
    /// Wraps <see cref="_db"/>.SaveChangesAsync with diagnostics that a bare try/catch around
    /// SaveChangesAsync doesn't give you for free: which entities/rows were being written and,
    /// for a Postgres constraint violation, the actual constraint name and detail (buried in
    /// <see cref="DbUpdateException.InnerException"/>, not the outer exception's message). Logs
    /// then rethrows unchanged — <see cref="ProcessAsync"/>'s outer catch is still what decides
    /// Status=Failed and triggers the credit refund.
    /// </summary>
    private async Task SaveChangesWithDiagnosticsAsync(Guid documentId, string stage, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            LogSaveChangesFailure(documentId, stage, ex);
            throw;
        }
    }

    private void LogSaveChangesFailure(Guid documentId, string stage, DbUpdateException ex)
    {
        var entries = string.Join(", ", ex.Entries.Select(e => $"{e.Entity.GetType().Name}[{e.State}]"));

        if (ex.InnerException is PostgresException pgEx)
        {
            _logger.LogError(
                ex,
                "SaveChanges failed for document {DocumentId} at stage '{Stage}'. " +
                "Postgres {SqlState} on constraint '{ConstraintName}' (table '{Table}'): {Detail}. Entries: {Entries}",
                documentId,
                stage,
                pgEx.SqlState,
                pgEx.ConstraintName,
                pgEx.TableName,
                pgEx.Detail,
                entries);
        }
        else
        {
            _logger.LogError(
                ex,
                "SaveChanges failed for document {DocumentId} at stage '{Stage}'. Inner exception: {InnerMessage}. Entries: {Entries}",
                documentId,
                stage,
                ex.InnerException?.Message ?? ex.Message,
                entries);
        }
    }

    /// <summary>
    /// A document reaching <see cref="DocumentStatus.Failed"/> is this pipeline's only notion of
    /// "processing failed permanently" — see the class remarks on retry behavior. Refunds the
    /// same amount that was charged for this attempt (same content-type-based pricing), so the
    /// organization is never left paying for a document that was never actually processed.
    /// Refund failures are logged but never allowed to fail the processing job itself.
    /// </summary>
    private async Task RefundProcessingCreditsAsync(Document document, CancellationToken ct)
    {
        var amount = CreditPricing.ResolveCost(document.ContentType, document.OriginalFileName, _creditOptions);
        try
        {
            await _creditService.RefundAsync(
                document.OrganizationId, amount, "Document", document.Id, "Processing failed permanently", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to refund {Amount} credit(s) for permanently failed document {DocumentId}", amount, document.Id);
        }
    }

    /// <summary>
    /// Which concrete strategy ends up serving a PDF (free text-layer read vs. paid OCR/LLM) is
    /// only known once extraction has actually run — unlike XML, which is priced free from the
    /// pre-charge at upload/reprocess time (see <see cref="CreditPricing.ResolveCost"/>). So the
    /// upload/reprocess charge point always holds the worst-case (OCR) price up front — preserving
    /// the pre-charge burn-loop defense — and this reconciles it down to the true price after the
    /// fact, refunding the difference when the actual provider turned out to be cheaper. A no-op
    /// (refund of 0) for XML and any provider priced at or above the pre-charge, computed the same
    /// way regardless of which strategy actually won.
    /// </summary>
    private async Task RefundFreePathDifferenceAsync(Document document, string actualProviderName, CancellationToken ct)
    {
        var preChargedAmount = CreditPricing.ResolveCost(document.ContentType, document.OriginalFileName, _creditOptions);
        var actualAmount = CreditPricing.ResolveActualCost(actualProviderName, _creditOptions);
        if (actualAmount >= preChargedAmount)
            return;

        var refundAmount = preChargedAmount - actualAmount;
        try
        {
            await _creditService.RefundAsync(
                document.OrganizationId, refundAmount, "Document", document.Id,
                $"Free processing path used ({actualProviderName})", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to refund {Amount} credit(s) for document {DocumentId} after free path ({ProviderName})",
                refundAmount, document.Id, actualProviderName);
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

        foreach (var taxLine in document.TaxBreakdowns.ToList())
            _db.InvoiceTaxBreakdowns.Remove(taxLine);
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
