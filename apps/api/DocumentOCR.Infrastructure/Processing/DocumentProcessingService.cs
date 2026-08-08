using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Orchestrates the full document processing pipeline: structured-format fast path (e.g. TT78
/// invoice XML) when available, OCR pipeline otherwise. Catches its own failures internally
/// (never rethrows), so <c>Status = Failed</c> set here is this system's only notion of
/// "processing failed permanently" — Hangfire's <c>[AutomaticRetry]</c> on the calling job never
/// actually re-runs a business-logic failure, only an exception this method didn't catch.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private const string StructuredInvoiceProviderName = CreditPricing.StructuredXmlProviderName;

    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorageService _storage;
    private readonly IDocumentOcrProvider _ocrProvider;
    private readonly IFieldExtractionService _extraction;
    private readonly IFieldNormalizationService _normalization;
    private readonly IFieldValidationService _validation;
    private readonly IStructuredInvoiceParser _structuredInvoiceParser;
    private readonly ICreditService _creditService;
    private readonly OcrOptions _ocrOptions;
    private readonly CreditOptions _creditOptions;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        IApplicationDbContext db,
        IDocumentStorageService storage,
        IDocumentOcrProvider ocrProvider,
        IFieldExtractionService extraction,
        IFieldNormalizationService normalization,
        IFieldValidationService validation,
        IStructuredInvoiceParser structuredInvoiceParser,
        ICreditService creditService,
        IOptions<OcrOptions> ocrOptions,
        IOptions<CreditOptions> creditOptions,
        ILogger<DocumentProcessingService> logger)
    {
        _db = db;
        _storage = storage;
        _ocrProvider = ocrProvider;
        _extraction = extraction;
        _normalization = normalization;
        _validation = validation;
        _structuredInvoiceParser = structuredInvoiceParser;
        _creditService = creditService;
        _ocrOptions = ocrOptions.Value;
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

            var useStructuredParser = _structuredInvoiceParser.CanParse(document.ContentType, document.OriginalFileName);
            var selectedProviderName = useStructuredParser ? StructuredInvoiceProviderName : _ocrProvider.ProviderName;

            // Permanent diagnostic log (not temporary) — the single place that records which
            // branch/provider a given upload actually took, so a stuck-in-Processing report can
            // be traced back to "which path did this take" without guessing.
            _logger.LogInformation(
                "Processing branch selected for document {DocumentId} ({FileName}, ContentType={ContentType}): " +
                "{Branch} via provider {ProviderName}",
                documentId,
                document.OriginalFileName,
                document.ContentType,
                useStructuredParser ? "StructuredXml" : "Ocr",
                selectedProviderName);

            if (useStructuredParser)
                await ProcessStructuredInvoiceAsync(document, fileStream, ct);
            else
                await ProcessViaOcrAsync(document, fileStream, ct);
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

    private async Task ProcessViaOcrAsync(Document document, Stream fileStream, CancellationToken ct)
    {
        var documentId = document.Id;

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

        var ocrResult = await AnalyzeWithTimeoutAsync(ocrInput, documentId, ct);

        _logger.LogInformation(
            "OCR provider {ProviderName} (Model={ModelId}) completed for document {DocumentId}. Success={Success}, Pages={PageCount}, DurationMs={ProcessingTimeMs}, EstimatedCost={EstimatedCost}",
            _ocrProvider.ProviderName,
            ocrResult.ModelId,
            documentId,
            ocrResult.Success,
            ocrResult.PageCount,
            ocrResult.ProcessingTimeMs,
            ocrResult.EstimatedCost);

        ocrResult = await PersistOcrArtifactsAsync(documentId, ocrResult, ct);

        _db.OcrProviderLogs.Add(new OcrProviderLog
        {
            DocumentId = documentId,
            // ocrResult.ProviderName (not _ocrProvider.ProviderName) so the audit trail reflects
            // which branch actually produced this result when the injected provider is
            // PdfProviderRouter (e.g. "PdfTextLayer" vs. the configured OCR provider it fell back to).
            ProviderName = ocrResult.ProviderName,
            ModelId = ocrResult.ModelId,
            PageCount = ocrResult.PageCount,
            ProcessingTimeMs = ocrResult.ProcessingTimeMs,
            EstimatedCost = ocrResult.EstimatedCost,
            Success = ocrResult.Success,
            ErrorMessage = ocrResult.ErrorMessage,
            RawResponseJson = _ocrOptions.StoreRawProviderResponse ? ocrResult.RawProviderResponseJson : null,
            RawResponsePath = ocrResult.RawProviderResponsePath,
            NormalizedResultPath = ocrResult.NormalizedOcrResultPath
        });

        if (!ocrResult.Success)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = string.IsNullOrWhiteSpace(ocrResult.ErrorMessage)
                ? "OCR provider failed without an error message."
                : ocrResult.ErrorMessage;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            await SaveChangesWithDiagnosticsAsync(documentId, "ProcessViaOcrAsync:FailureSave", ct);

            _logger.LogWarning(
                "Document {DocumentId} marked Failed because OCR provider {ProviderName} returned an unsuccessful result: {ErrorMessage}",
                documentId,
                _ocrProvider.ProviderName,
                document.ErrorMessage);

            await RefundProcessingCreditsAsync(document, ct);

            return;
        }

        await RefundFreePathDifferenceAsync(document, ocrResult.ProviderName, ct);

        foreach (var page in ocrResult.Pages)
        {
            _db.DocumentPages.Add(new DocumentPage
            {
                DocumentId = documentId,
                PageNumber = page.PageNumber,
                RawText = page.FullText
            });
        }

        document.TablesJson = ocrResult.Tables.Count > 0
            ? JsonSerializer.Serialize(ocrResult.Tables)
            : null;

        var extractedFields = _extraction.Extract(documentId, ocrResult);
        _logger.LogInformation(
            "Extracted {FieldCount} fields for document {DocumentId}",
            extractedFields.Count,
            documentId);

        _normalization.NormalizeFields(extractedFields);

        // "VatRate" is a candidate-only key (see FieldExtractionService.AddVatRateLineCandidate) —
        // consumed here to synthesize a tax-breakdown row, never persisted as an ExtractedField
        // itself (no profile field references it, so it would otherwise land in "Other detected
        // fields" for no reason).
        var vatRateField = extractedFields.FirstOrDefault(f => f.FieldName == "VatRate");
        var persistedFields = extractedFields.Where(f => f.FieldName != "VatRate").ToList();

        foreach (var field in persistedFields)
            _db.ExtractedFields.Add(field);

        foreach (var taxLine in BuildOcrTaxBreakdown(documentId, vatRateField, persistedFields))
            _db.InvoiceTaxBreakdowns.Add(taxLine);

        var warnings = _validation.Validate(documentId, persistedFields);
        foreach (var warning in warnings)
            _db.ValidationWarnings.Add(warning);

        document.DocumentType = GetDetectedDocumentType(persistedFields);
        document.Status = DocumentStatus.Processed;
        document.PageCount = ocrResult.PageCount;
        document.ProcessingCompletedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        await SaveChangesWithDiagnosticsAsync(documentId, "ProcessViaOcrAsync:FinalSave", ct);

        _logger.LogInformation(
            "Document {DocumentId} processed successfully. Status={Status}, DocumentType={DocumentType}, Pages={PageCount}, Fields={FieldCount}, Warnings={WarningCount}",
            documentId,
            document.Status,
            document.DocumentType,
            document.PageCount,
            extractedFields.Count,
            warnings.Count);
    }

    /// <summary>
    /// Hard pipeline-level ceiling (<see cref="OcrOptions.CallTimeoutSeconds"/>, default 120s) on
    /// a single <see cref="IDocumentOcrProvider.AnalyzeAsync"/> call, independent of whatever
    /// timeout (if any) the provider enforces internally -- so a provider that hangs, or a future
    /// provider that forgets its own timeout, can never leave a document stuck in
    /// <see cref="DocumentStatus.Processing"/> forever. On expiry this returns a synthetic failed
    /// result rather than throwing, so the normal "!ocrResult.Success" handling in
    /// <see cref="ProcessViaOcrAsync"/> (Failed status + refund) takes care of the rest.
    /// </summary>
    private async Task<NormalizedOcrDocument> AnalyzeWithTimeoutAsync(
        DocumentInput input, Guid documentId, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_ocrOptions.CallTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await _ocrProvider.AnalyzeAsync(input, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError(
                "OCR provider {ProviderName} did not respond within {TimeoutSeconds}s for document {DocumentId}.",
                _ocrProvider.ProviderName, _ocrOptions.CallTimeoutSeconds, documentId);

            return new NormalizedOcrDocument
            {
                Success = false,
                ProviderName = _ocrProvider.ProviderName,
                ErrorMessage =
                    $"OCR provider {_ocrProvider.ProviderName} did not respond within " +
                    $"{_ocrOptions.CallTimeoutSeconds}s."
            };
        }
    }

    private async Task ProcessStructuredInvoiceAsync(Document document, Stream fileStream, CancellationToken ct)
    {
        var documentId = document.Id;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Parsing structured invoice XML for document {DocumentId} via {ProviderName}",
            documentId,
            StructuredInvoiceProviderName);

        var parseResult = await _structuredInvoiceParser.ParseAsync(fileStream, ct);
        stopwatch.Stop();

        var rawXmlPath = await PersistRawXmlArtifactAsync(documentId, parseResult.RawXml, ct);

        _db.OcrProviderLogs.Add(new OcrProviderLog
        {
            DocumentId = documentId,
            ProviderName = StructuredInvoiceProviderName,
            ModelId = StructuredInvoiceProviderName,
            PageCount = 1,
            ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            EstimatedCost = 0m,
            Success = parseResult.Success,
            ErrorMessage = parseResult.ErrorMessage,
            RawResponseJson = _ocrOptions.StoreRawProviderResponse ? JsonSerializer.Serialize(parseResult.RawXml) : null,
            RawResponsePath = rawXmlPath
        });

        if (!parseResult.Success)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = string.IsNullOrWhiteSpace(parseResult.ErrorMessage)
                ? "Structured invoice parser failed without an error message."
                : parseResult.ErrorMessage;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            await SaveChangesWithDiagnosticsAsync(documentId, "ProcessStructuredInvoiceAsync:FailureSave", ct);

            _logger.LogWarning(
                "Document {DocumentId} marked Failed because structured XML parsing failed: {ErrorMessage}",
                documentId,
                document.ErrorMessage);

            await RefundProcessingCreditsAsync(document, ct);

            return;
        }

        var extractedFields = parseResult.Fields;
        foreach (var field in extractedFields)
        {
            field.DocumentId = documentId;
            _db.ExtractedFields.Add(field);
        }

        _normalization.NormalizeFields(extractedFields);

        foreach (var taxLine in parseResult.TaxBreakdown)
        {
            taxLine.DocumentId = documentId;
            _db.InvoiceTaxBreakdowns.Add(taxLine);
        }

        var warnings = _validation.Validate(documentId, extractedFields);
        foreach (var warning in warnings)
            _db.ValidationWarnings.Add(warning);

        // No OCR pages exist for a structured XML source, unlike the OCR pipeline which always
        // produces at least one DocumentPage.
        document.TablesJson = null;
        document.DocumentType = GetDetectedDocumentType(extractedFields);
        document.Status = DocumentStatus.Processed;
        document.PageCount = 1;
        document.ProcessingCompletedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        await SaveChangesWithDiagnosticsAsync(documentId, "ProcessStructuredInvoiceAsync:FinalSave", ct);

        _logger.LogInformation(
            "Document {DocumentId} processed via structured XML parser. Status={Status}, DocumentType={DocumentType}, Fields={FieldCount}, Warnings={WarningCount}",
            documentId,
            document.Status,
            document.DocumentType,
            extractedFields.Count,
            warnings.Count);
    }

    private async Task<string?> PersistRawXmlArtifactAsync(Guid documentId, string? rawXml, CancellationToken ct)
    {
        if (!_ocrOptions.StoreRawProviderResponse || string.IsNullOrWhiteSpace(rawXml))
            return null;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawXml));
        return await _storage.SaveAsync(stream, $"{documentId}-source.xml", "application/xml", ct);
    }

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes the raw provider response and/or the full normalized OCR result to storage
    /// (gated by <see cref="OcrOptions.StoreRawProviderResponse"/> / <see cref="OcrOptions.StoreNormalizedOcrResult"/>)
    /// and returns an updated <see cref="NormalizedOcrDocument"/> carrying their storage paths.
    /// A provider that returned an unsuccessful result has nothing meaningful to persist here.
    /// </summary>
    private async Task<NormalizedOcrDocument> PersistOcrArtifactsAsync(
        Guid documentId, NormalizedOcrDocument ocrResult, CancellationToken ct)
    {
        if (!ocrResult.Success) return ocrResult;

        string? rawPath = null;
        if (_ocrOptions.StoreRawProviderResponse && !string.IsNullOrWhiteSpace(ocrResult.RawProviderResponseJson))
        {
            rawPath = await WriteJsonArtifactAsync(
                ocrResult.RawProviderResponseJson, $"{documentId}-raw-response.json", ct);
        }

        string? normalizedPath = null;
        if (_ocrOptions.StoreNormalizedOcrResult)
        {
            var normalizedJson = JsonSerializer.Serialize(ocrResult, ArtifactJsonOptions);
            normalizedPath = await WriteJsonArtifactAsync(
                normalizedJson, $"{documentId}-normalized-ocr-result.json", ct);
        }

        return ocrResult with
        {
            RawProviderResponsePath = rawPath,
            NormalizedOcrResultPath = normalizedPath
        };
    }

    private async Task<string> WriteJsonArtifactAsync(string json, string fileName, CancellationToken ct)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await _storage.SaveAsync(stream, fileName, "application/json", ct);
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
    /// Whether a PDF ends up served for free (<c>PdfTextLayer</c>, reading the PDF's own embedded
    /// text layer) or via paid OCR is only known once <see cref="IDocumentOcrProvider"/> has
    /// actually run — unlike XML, which is priced free from the pre-charge at upload/reprocess
    /// time (see <see cref="CreditPricing.ResolveCost"/>). So the upload/reprocess charge point
    /// always holds the worst-case (OCR) price up front — preserving the pre-charge burn-loop
    /// defense — and this reconciles it down to the true price after the fact, refunding the
    /// difference when the actual provider turned out to be free.
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

    /// <summary>
    /// Synthesizes a single tax-breakdown row from the OCR pipeline's best-effort "VatRate" line
    /// candidate plus whatever Subtotal/VAT amounts were already extracted — mirrors what TT78 XML
    /// always gets deterministically from THTTLTSuat, but only when a document-level rate was
    /// actually recognized; otherwise the document simply has no breakdown rows (not a fabricated
    /// one at 0/unknown).
    /// </summary>
    private List<InvoiceTaxBreakdown> BuildOcrTaxBreakdown(
        Guid documentId, ExtractedField? vatRateField, IReadOnlyList<ExtractedField> fields)
    {
        if (vatRateField is null) return [];

        var normalizedRate = _normalization.NormalizeVatRate(vatRateField.RawValue);
        if (normalizedRate is null) return [];

        var subtotal = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.SubtotalAmount));
        var vat = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.VatAmount));

        return
        [
            new InvoiceTaxBreakdown
            {
                DocumentId = documentId,
                RawVatRate = vatRateField.RawValue,
                VatRate = normalizedRate,
                TaxableAmount = ParseDecimalOrNull(subtotal?.NormalizedValue),
                TaxAmount = ParseDecimalOrNull(vat?.NormalizedValue),
                Confidence = vatRateField.Confidence,
                SortOrder = 0
            }
        ];
    }

    private static decimal? ParseDecimalOrNull(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static DocumentType GetDetectedDocumentType(IEnumerable<ExtractedField> fields)
    {
        var documentTypeField = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.DocumentType));
        var value = documentTypeField?.NormalizedValue ?? documentTypeField?.RawValue;

        return Enum.TryParse<DocumentType>(value, ignoreCase: true, out var documentType)
            ? documentType
            : DocumentType.Unknown;
    }
}
