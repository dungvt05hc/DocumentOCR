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
    private readonly DocumentReviewMappingService _reviewMapping;
    private readonly IReviewTableBuilder _tableBuilder;

    public DocumentService(
        IApplicationDbContext db,
        IDocumentStorageService storage,
        IFieldValidationService validation,
        DocumentReviewMappingService reviewMapping,
        IReviewTableBuilder tableBuilder)
    {
        _db = db;
        _storage = storage;
        _validation = validation;
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

    public async Task<List<DocumentDto>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
    {
        var docs = await _db.Documents
            .Where(d => d.OrganizationId == organizationId)
            .Include(d => d.ValidationWarnings)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return docs.Select(MapToDto).ToList();
    }

    public async Task<DocumentDetailDto?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLogs)
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

    public async Task UpdateFieldsAsync(Guid documentId, Guid organizationId, UpdateFieldsRequest request, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
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

            field.NormalizedValue = update.NormalizedValue;
            field.RawValue = update.RawValue ?? field.RawValue;
            field.IsEditedByUser = true;
            field.EditedAt = DateTime.UtcNow;
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
        OriginalFileName = d.OriginalFileName,
        ContentType = d.ContentType,
        FileSizeBytes = d.FileSizeBytes,
        PageCount = d.PageCount,
        Status = d.Status,
        DocumentType = d.DocumentType,
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
        OriginalFileName = d.OriginalFileName,
        ContentType = d.ContentType,
        FileSizeBytes = d.FileSizeBytes,
        PageCount = d.PageCount,
        Status = d.Status,
        DocumentType = d.DocumentType,
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
