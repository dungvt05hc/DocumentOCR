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

    public DocumentService(
        IApplicationDbContext db,
        IDocumentStorageService storage)
    {
        _db = db;
        _storage = storage;
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

    public async Task<DocumentDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLog)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (doc is null) return null;
        return MapToDetailDto(doc);
    }

    public async Task MarkUploadedForProcessingAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FindAsync([documentId], ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        doc.Status = DocumentStatus.Uploaded;
        doc.ErrorMessage = null;
        doc.ProcessingStartedAt = null;
        doc.ProcessingCompletedAt = null;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateFieldsAsync(Guid documentId, UpdateFieldsRequest request, CancellationToken ct = default)
    {
        var doc = await _db.Documents
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        foreach (var update in request.Fields)
        {
            var field = doc.Fields.FirstOrDefault(f => f.FieldName == update.FieldName);
            if (field is null)
            {
                field = new ExtractedField { DocumentId = documentId, FieldName = update.FieldName };
                _db.ExtractedFields.Add(field);
            }

            field.NormalizedValue = update.NormalizedValue;
            field.IsEditedByUser = true;
            field.EditedAt = DateTime.UtcNow;
            field.UpdatedAt = DateTime.UtcNow;
        }

        doc.Status = DocumentStatus.Reviewed;
        doc.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> DownloadOriginalAsync(
        Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FindAsync([documentId], ct)
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
        OcrLog = d.OcrProviderLog is null ? null : new OcrProviderLogDto
        {
            ProviderName = d.OcrProviderLog.ProviderName,
            PageCount = d.OcrProviderLog.PageCount,
            ProcessingTimeMs = d.OcrProviderLog.ProcessingTimeMs,
            EstimatedCost = d.OcrProviderLog.EstimatedCost,
            Success = d.OcrProviderLog.Success,
            ErrorMessage = d.OcrProviderLog.ErrorMessage
        }
    };
}
