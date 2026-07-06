using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

public class DocumentService
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentStorageService _storage;
    private readonly IFieldValidationService _validation;

    public DocumentService(
        IApplicationDbContext db,
        IDocumentStorageService storage,
        IFieldValidationService validation)
    {
        _db = db;
        _storage = storage;
        _validation = validation;
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
            var field = doc.Fields.FirstOrDefault(f => f.FieldName == update.FieldName);
            if (field is null)
            {
                if (!Enum.TryParse<FieldName>(update.FieldName, out _))
                    throw new ArgumentException($"'{update.FieldName}' is not a recognized field name.");

                field = new ExtractedField { DocumentId = documentId, FieldName = update.FieldName };
                doc.Fields.Add(field);
                _db.ExtractedFields.Add(field);
            }

            field.NormalizedValue = update.NormalizedValue;
            field.IsEditedByUser = true;
            field.EditedAt = DateTime.UtcNow;
            field.UpdatedAt = DateTime.UtcNow;
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
            ErrorMessage = latest.ErrorMessage
        };
    }
}
