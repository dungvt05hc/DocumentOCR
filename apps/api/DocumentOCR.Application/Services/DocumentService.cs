using System.Text.Json;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

public class DocumentService
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorageService _storage;
    private readonly IFieldValidationService _validation;
    private readonly IFieldNormalizationService _normalization;
    private readonly IClientAutoSuggestService _clientAutoSuggest;
    private readonly DocumentReviewMappingService _reviewMapping;
    private readonly IReviewTableBuilder _tableBuilder;

    public DocumentService(
        IApplicationDbContext db,
        IDocumentStorageService storage,
        IFieldValidationService validation,
        IFieldNormalizationService normalization,
        IClientAutoSuggestService clientAutoSuggest,
        DocumentReviewMappingService reviewMapping,
        IReviewTableBuilder tableBuilder)
    {
        _db = db;
        _storage = storage;
        _validation = validation;
        _normalization = normalization;
        _clientAutoSuggest = clientAutoSuggest;
        _reviewMapping = reviewMapping;
        _tableBuilder = tableBuilder;
    }

    public async Task<DocumentDto> UploadAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var storedPath = await _storage.SaveAsync(fileStream, originalFileName, contentType, ct);

        var document = new Document
        {
            OrganizationId = organizationId,
            OriginalFileName = originalFileName,
            StoredFilePath = storedPath,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Status = DocumentStatus.Uploaded
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        return MapToDto(document);
    }

    /// <summary>
    /// <paramref name="invoiceDateFrom"/>/<paramref name="invoiceDateTo"/> filter by the document's
    /// normalized "InvoiceDate" extracted field (stored as an ISO <c>yyyy-MM-dd</c> string, which
    /// sorts identically to a real date comparison), falling back to <see cref="Document.CreatedAt"/>
    /// for documents that have no InvoiceDate field yet (e.g. still processing, or extraction
    /// couldn't find one).
    /// </summary>
    public async Task<List<DocumentDto>> GetAllAsync(
        Guid organizationId,
        Guid? clientProfileId = null,
        DateOnly? invoiceDateFrom = null,
        DateOnly? invoiceDateTo = null,
        CancellationToken ct = default)
    {
        var query = _db.Documents
            .Where(d => d.OrganizationId == organizationId)
            .Include(d => d.ClientProfile)
            .Include(d => d.ValidationWarnings)
            .AsQueryable();

        if (clientProfileId is not null)
            query = query.Where(d => d.ClientProfileId == clientProfileId);

        if (invoiceDateFrom is not null || invoiceDateTo is not null)
        {
            query = query.Include(d => d.Fields);
        }

        var docs = await query.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);

        if (invoiceDateFrom is not null || invoiceDateTo is not null)
        {
            docs = docs.Where(d => IsWithinInvoiceDateRange(d, invoiceDateFrom, invoiceDateTo)).ToList();
        }

        return docs.Select(MapToDto).ToList();
    }

    private static bool IsWithinInvoiceDateRange(Document d, DateOnly? from, DateOnly? to)
    {
        var effectiveDate = GetEffectiveInvoiceDate(d);
        if (from is not null && effectiveDate < from) return false;
        if (to is not null && effectiveDate > to) return false;
        return true;
    }

    private static DateOnly GetEffectiveInvoiceDate(Document d)
    {
        var invoiceDateValue = d.Fields
            .FirstOrDefault(f => f.FieldName == nameof(FieldName.InvoiceDate))?.NormalizedValue;

        return DateOnly.TryParse(invoiceDateValue, out var invoiceDate)
            ? invoiceDate
            : DateOnly.FromDateTime(d.CreatedAt);
    }

    public async Task AssignClientAsync(
        Guid documentId, Guid organizationId, Guid? clientProfileId, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        if (clientProfileId is not null)
        {
            var clientExists = await _db.ClientProfiles.AnyAsync(
                c => c.Id == clientProfileId && c.OrganizationId == organizationId, ct);
            if (!clientExists)
                throw new KeyNotFoundException($"Client {clientProfileId} not found.");
        }

        doc.ClientProfileId = clientProfileId;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Re-derive Direction against whichever client is now attached (or none) — keeps it
        // consistent with a manual re-assignment rather than leaving a stale value from before.
        await _clientAutoSuggest.InferDirectionAsync(documentId, ct);
    }

    /// <summary>Manual override for <see cref="Document.Direction"/> — the auto-inferred value (see <see cref="IClientAutoSuggestService.InferDirectionAsync"/>) is only a suggestion the user can always correct.</summary>
    public async Task SetDirectionAsync(Guid documentId, Guid organizationId, DocumentDirection direction, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        doc.Direction = direction;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DocumentDetailDto?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLogs)
            .Include(d => d.ClientProfile)
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId, ct);

        if (doc is null) return null;
        return MapToDetailDto(doc);
    }

    /// <summary>Dynamic, document-category-driven review response — see <see cref="DocumentReviewMappingService"/>.</summary>
    public async Task<DocumentReviewResponse?> GetReviewByIdAsync(
        Guid id, Guid organizationId, bool includeDebugSummary = false, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLogs)
            .Include(d => d.Pages)
            .Include(d => d.TaxBreakdowns)
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId, ct);

        if (doc is null) return null;
        return _reviewMapping.Map(doc, includeDebugSummary);
    }

    /// <summary>
    /// Full development/debug view for <c>GET /api/documents/{id}/ocr-debug</c>. Full text and
    /// detected tables always come from what's already persisted (<see cref="DocumentPage.RawText"/>/
    /// <see cref="Document.TablesJson"/>); lines/paragraphs/key-value pairs are only available
    /// when the optional normalized-OCR-result blob was stored (<c>Ocr:StoreNormalizedOcrResult</c>)
    /// — reloaded best-effort here, never failing the request if it's missing or unreadable.
    /// </summary>
    public async Task<OcrDebugResponse?> GetOcrDebugAsync(
        Guid id, Guid organizationId, bool exposeRawJson, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLogs)
            .Include(d => d.Pages)
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId, ct);

        if (doc is null) return null;

        var latestLog = doc.OcrProviderLogs.OrderByDescending(l => l.CreatedAt).FirstOrDefault();
        var tables = _tableBuilder.BuildTables(DeserializeTables(doc.TablesJson));

        var response = new OcrDebugResponse
        {
            DocumentId = doc.Id,
            FullText = string.Join("\n\n", doc.Pages.OrderBy(p => p.PageNumber).Select(p => p.RawText)),
            Tables = tables,
            Fields = doc.Fields.Select(MapToFieldDto).ToList(),
            Warnings = doc.ValidationWarnings.Select(MapToWarningDto).ToList(),
            RawProviderResponsePath = latestLog?.RawResponsePath,
            NormalizedOcrResultPath = latestLog?.NormalizedResultPath,
            OcrSummary = $"{doc.PageCount} page(s), {tables.Count} table(s), {doc.Fields.Count} field(s), {doc.ValidationWarnings.Count} warning(s)"
        };

        if (!string.IsNullOrWhiteSpace(latestLog?.NormalizedResultPath))
        {
            await TryEnrichFromNormalizedResultAsync(response, latestLog.NormalizedResultPath, exposeRawJson, ct);
        }

        return response;
    }

    private async Task TryEnrichFromNormalizedResultAsync(
        OcrDebugResponse response, string normalizedResultPath, bool exposeRawJson, CancellationToken ct)
    {
        try
        {
            await using var stream = await _storage.GetStreamAsync(normalizedResultPath, ct);
            var normalized = await JsonSerializer.DeserializeAsync<NormalizedOcrDocument>(stream, cancellationToken: ct);
            if (normalized is null) return;

            response.Lines = normalized.Lines.Select(l => l.Text).ToList();
            response.Paragraphs = normalized.Paragraphs.Select(p => p.Text).ToList();
            response.KeyValuePairs = normalized.KeyValuePairs
                .Select(kv => new OcrDebugKeyValuePair { Key = kv.KeyText, Value = kv.ValueText, Confidence = kv.Confidence })
                .ToList();

            if (exposeRawJson)
            {
                response.RawProviderResponseJson = normalized.RawProviderResponseJson;
            }
        }
        catch (Exception)
        {
            // Best-effort enrichment only — a missing/corrupt/unreadable blob must not fail the
            // debug request, which already has the always-available subset (full text, tables,
            // fields, warnings) to show.
        }
    }

    private static IReadOnlyList<OcrTable> DeserializeTables(string? tablesJson)
    {
        if (string.IsNullOrWhiteSpace(tablesJson)) return [];
        return JsonSerializer.Deserialize<List<OcrTable>>(tablesJson) ?? [];
    }

    /// <summary>
    /// Patches edited cell text into <paramref name="tables"/> in place, addressed by the same
    /// TableId ("table-{index}")/RowIndex/ColumnKey the review response handed the client.
    /// <see cref="OcrTable"/>/<see cref="OcrTableCell"/> are immutable (init-only), so a matched
    /// cell/table is replaced rather than mutated. Unresolvable table/row/column references are
    /// silently skipped — a stale edit from the client must never fail the whole save.
    /// </summary>
    private static void ApplyTableEdits(List<OcrTable> tables, List<TableUpdateItem> updates, IReviewTableBuilder tableBuilder)
    {
        foreach (var update in updates)
        {
            if (!TryParseTableIndex(update.TableId, out var index) || index < 0 || index >= tables.Count) continue;

            var table = tables[index];
            var reviewTable = tableBuilder.BuildTables([table]).FirstOrDefault();
            if (reviewTable is null) continue;

            var columnIndexByKey = reviewTable.Columns.ToDictionary(c => c.ColumnKey, c => c.ColumnIndex);
            var cells = table.Cells.ToList();

            foreach (var cellUpdate in update.Rows.SelectMany(row => row.Cells.Select(cell => (row.RowIndex, cell))))
            {
                if (cellUpdate.cell.ColumnKey is null
                    || !columnIndexByKey.TryGetValue(cellUpdate.cell.ColumnKey, out var columnIndex))
                {
                    continue;
                }

                var cellIndex = cells.FindIndex(c => c.RowIndex == cellUpdate.RowIndex && c.ColumnIndex == columnIndex);
                if (cellIndex < 0) continue;

                var existing = cells[cellIndex];
                cells[cellIndex] = new OcrTableCell
                {
                    RowIndex = existing.RowIndex,
                    ColumnIndex = existing.ColumnIndex,
                    RowSpan = existing.RowSpan,
                    ColumnSpan = existing.ColumnSpan,
                    Text = cellUpdate.cell.Text,
                    Kind = existing.Kind,
                    BoundingBox = existing.BoundingBox
                };
            }

            tables[index] = new OcrTable
            {
                PageNumber = table.PageNumber,
                RowCount = table.RowCount,
                ColumnCount = table.ColumnCount,
                Cells = cells,
                BoundingBox = table.BoundingBox
            };
        }
    }

    private static bool TryParseTableIndex(string tableId, out int index)
    {
        const string prefix = "table-";
        index = -1;
        return tableId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(tableId.AsSpan(prefix.Length), out index);
    }

    /// <summary>
    /// Replaces <paramref name="doc"/>'s tax-breakdown rows with <paramref name="updates"/> as a
    /// whole set (not a delta): rows with a matching <see cref="TaxBreakdownUpdateItem.Id"/> are
    /// updated in place, rows with no <c>Id</c> are new, and any existing row absent from
    /// <paramref name="updates"/> is deleted — the natural shape for a review-UI table that always
    /// submits its current full row set (add/edit/remove rows freely).
    /// </summary>
    private void ApplyTaxBreakdownEdits(Document doc, List<TaxBreakdownUpdateItem> updates)
    {
        var keptIds = updates.Where(u => u.Id is not null).Select(u => u.Id!.Value).ToHashSet();

        foreach (var stale in doc.TaxBreakdowns.Where(t => !keptIds.Contains(t.Id)).ToList())
        {
            doc.TaxBreakdowns.Remove(stale);
            _db.InvoiceTaxBreakdowns.Remove(stale);
        }

        foreach (var update in updates)
        {
            var row = update.Id is not null ? doc.TaxBreakdowns.FirstOrDefault(t => t.Id == update.Id) : null;
            if (row is null)
            {
                row = new InvoiceTaxBreakdown { DocumentId = doc.Id };
                doc.TaxBreakdowns.Add(row);
                _db.InvoiceTaxBreakdowns.Add(row);
            }

            row.RawVatRate = update.VatRate;
            row.VatRate = _normalization.NormalizeVatRate(update.VatRate);
            row.TaxableAmount = update.TaxableAmount;
            row.TaxAmount = update.TaxAmount;
            row.SortOrder = update.SortOrder;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static ExtractedFieldDto MapToFieldDto(ExtractedField f) => new()
    {
        Id = f.Id,
        FieldName = f.FieldName,
        RawValue = f.RawValue,
        NormalizedValue = f.NormalizedValue,
        Confidence = f.Confidence,
        PageNumber = f.PageNumber,
        ExtractionMethod = f.ExtractionMethod,
        SourceText = f.SourceText,
        SourceType = f.SourceType,
        ProviderFieldName = f.ProviderFieldName,
        IsRequired = f.IsRequired,
        IsEditedByUser = f.IsEditedByUser,
        EditedAt = f.EditedAt
    };

    private static ValidationWarningDto MapToWarningDto(ValidationWarning w) => new()
    {
        Id = w.Id,
        FieldName = w.FieldName,
        WarningCode = w.WarningCode,
        Severity = w.Severity,
        Message = w.Message
    };

    public async Task MarkUploadedForProcessingAsync(Guid documentId, Guid organizationId, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        doc.Status = DocumentStatus.Uploaded;
        doc.ErrorMessage = null;
        doc.ProcessingStartedAt = null;
        doc.ProcessingCompletedAt = null;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records why a just-uploaded document was never enqueued for processing (insufficient
    /// credit balance / daily cap) directly on the persisted row, so it survives past the
    /// upload response — the document otherwise looks identical to a normal "just uploaded, not
    /// yet processed" row (<see cref="DocumentStatus.Uploaded"/>, no <see cref="Document.ErrorMessage"/>)
    /// and the frontend has no way to distinguish the two.
    /// </summary>
    public async Task MarkBlockedByCreditAsync(Guid documentId, Guid organizationId, string message, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        doc.ErrorMessage = message;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateFieldsAsync(Guid documentId, Guid organizationId, UpdateFieldsRequest request, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.TaxBreakdowns)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        foreach (var update in request.Fields)
        {
            if (string.IsNullOrWhiteSpace(update.FieldName))
                throw new ArgumentException("Field updates must include a non-empty FieldName.");

            var field = doc.Fields.FirstOrDefault(f => f.FieldName == update.FieldName);
            if (field is null)
            {
                // FieldName is no longer restricted to the legacy FieldName enum — dynamic
                // review profiles (see IDocumentProfileCatalog) define field keys (e.g.
                // "BuyerName", "PONumber") that have no extractor yet, so the user must be able
                // to create them here when filling in a field the profile shows as missing.
                field = new ExtractedField { DocumentId = documentId, FieldName = update.FieldName };
                doc.Fields.Add(field);
                _db.ExtractedFields.Add(field);
            }

            // Only flag as user-edited when the submitted value actually differs from what's
            // currently stored (trimmed compare) — the review UI resubmits every field on every
            // save, not just the ones the user touched, so an unconditional "always edited" here
            // would mislabel untouched fields and, on repeat saves, would never let the flag
            // reflect reality. Confidence/RawValue (the machine-read audit trail) are never
            // touched here, so they survive edits unchanged.
            if (!ValuesEqual(field.NormalizedValue ?? field.RawValue, update.NormalizedValue))
            {
                field.IsEditedByUser = true;
                field.EditedAt = DateTime.UtcNow;
            }

            field.NormalizedValue = update.NormalizedValue;
            field.RawValue = update.RawValue ?? field.RawValue;
            field.UpdatedAt = DateTime.UtcNow;
        }

        // LineItems are intentionally not persisted (see UpdateFieldsRequest.LineItems) — always
        // re-derived from TablesJson, so there is nothing to save here.
        if (request.Tables.Count > 0)
        {
            var tables = DeserializeTables(doc.TablesJson).ToList();
            ApplyTableEdits(tables, request.Tables, _tableBuilder);
            doc.TablesJson = tables.Count > 0 ? JsonSerializer.Serialize(tables) : null;
        }

        if (request.TaxBreakdown is not null)
            ApplyTaxBreakdownEdits(doc, request.TaxBreakdown);

        // Warnings were computed against the original OCR output; the user's corrections
        // may have resolved some and none of the old ones still reflect the current data,
        // so the whole warning set is replaced rather than left stale.
        foreach (var warning in doc.ValidationWarnings.ToList())
            _db.ValidationWarnings.Remove(warning);

        foreach (var warning in _validation.Validate(documentId, doc.Fields))
            _db.ValidationWarnings.Add(warning);

        doc.Status = DocumentStatus.Reviewed;
        doc.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Trim-and-blank-insensitive equality — "so sánh sau khi trim và chuẩn hoá" for the IsEditedByUser check above.</summary>
    private static bool ValuesEqual(string? a, string? b)
    {
        var normalizedA = string.IsNullOrWhiteSpace(a) ? null : a.Trim();
        var normalizedB = string.IsNullOrWhiteSpace(b) ? null : b.Trim();
        return string.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> DownloadOriginalAsync(
        Guid documentId, Guid organizationId, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        var stream = await _storage.GetStreamAsync(doc.StoredFilePath, ct);
        return (stream, doc.ContentType, doc.OriginalFileName);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────────

    private static DocumentDto MapToDto(Document d) => new()
    {
        Id = d.Id,
        ClientProfileId = d.ClientProfileId,
        ClientProfileName = d.ClientProfile?.Name,
        OriginalFileName = d.OriginalFileName,
        ContentType = d.ContentType,
        FileSizeBytes = d.FileSizeBytes,
        PageCount = d.PageCount,
        Status = d.Status,
        DocumentType = d.DocumentType,
        Direction = d.Direction,
        ErrorMessage = d.ErrorMessage,
        WarningCount = d.ValidationWarnings.Count,
        ProcessingStartedAt = d.ProcessingStartedAt,
        ProcessingCompletedAt = d.ProcessingCompletedAt,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };

    private static DocumentDetailDto MapToDetailDto(Document d) => new()
    {
        Id = d.Id,
        ClientProfileId = d.ClientProfileId,
        ClientProfileName = d.ClientProfile?.Name,
        OriginalFileName = d.OriginalFileName,
        ContentType = d.ContentType,
        FileSizeBytes = d.FileSizeBytes,
        PageCount = d.PageCount,
        Status = d.Status,
        DocumentType = d.DocumentType,
        Direction = d.Direction,
        ErrorMessage = d.ErrorMessage,
        WarningCount = d.ValidationWarnings.Count,
        ProcessingStartedAt = d.ProcessingStartedAt,
        ProcessingCompletedAt = d.ProcessingCompletedAt,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        Fields = d.Fields.Select(f => new ExtractedFieldDto
        {
            Id = f.Id,
            FieldName = f.FieldName,
            RawValue = f.RawValue,
            NormalizedValue = f.NormalizedValue,
            Confidence = f.Confidence,
            PageNumber = f.PageNumber,
            ExtractionMethod = f.ExtractionMethod,
            SourceText = f.SourceText,
            SourceType = f.SourceType,
            ProviderFieldName = f.ProviderFieldName,
            IsRequired = f.IsRequired,
            IsEditedByUser = f.IsEditedByUser,
            EditedAt = f.EditedAt
        }).ToList(),
        Warnings = d.ValidationWarnings.Select(w => new ValidationWarningDto
        {
            Id = w.Id,
            FieldName = w.FieldName,
            WarningCode = w.WarningCode,
            Severity = w.Severity,
            Message = w.Message
        }).ToList(),
        OcrLog = MapLatestOcrLog(d.OcrProviderLogs)
    };

    private static OcrProviderLogDto? MapLatestOcrLog(IEnumerable<OcrProviderLog> logs)
    {
        var latest = logs.OrderByDescending(l => l.CreatedAt).FirstOrDefault();
        if (latest is null) return null;

        return new OcrProviderLogDto
        {
            ProviderName = latest.ProviderName,
            PageCount = latest.PageCount,
            ProcessingTimeMs = latest.ProcessingTimeMs,
            EstimatedCost = latest.EstimatedCost,
            Success = latest.Success,
            ErrorMessage = latest.ErrorMessage,
            RawResponsePath = latest.RawResponsePath,
            NormalizedResultPath = latest.NormalizedResultPath
        };
    }
}
