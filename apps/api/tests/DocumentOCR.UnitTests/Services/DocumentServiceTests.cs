using System.Text.Json;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Profiles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentOCR.UnitTests.Services;

public class DocumentServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task UpdateFieldsAsync_CorrectingInvalidTaxCode_RemovesStaleWarning()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.Fields.Add(Field(doc.Id, FieldName.SupplierTaxCode, "12345"));
            doc.ValidationWarnings.Add(Warning(doc.Id, FieldName.SupplierTaxCode, "INVALID_TAX_CODE_LENGTH", ValidationSeverity.Warning));
        });
        var sut = CreateSut(db);

        await sut.UpdateFieldsAsync(document.Id, OrganizationId, RequestFor(FieldName.SupplierTaxCode, "0100109106"));

        var reloaded = await db.Documents.Include(d => d.ValidationWarnings).SingleAsync(d => d.Id == document.Id);
        Assert.DoesNotContain(reloaded.ValidationWarnings, w => w.WarningCode == "INVALID_TAX_CODE_LENGTH");
    }

    [Fact]
    public async Task UpdateFieldsAsync_UnrelatedFieldStillMissing_RecreatesRequiredFieldWarning()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.Fields.Add(Field(doc.Id, FieldName.SupplierName, "OLD NAME"));
            doc.ValidationWarnings.Add(Warning(doc.Id, FieldName.InvoiceNumber, "REQUIRED_FIELD_MISSING", ValidationSeverity.High));
        });
        var sut = CreateSut(db);

        // Correcting an unrelated field must not make the still-missing InvoiceNumber
        // warning disappear along with the stale set — it has to be recomputed, not just cleared.
        await sut.UpdateFieldsAsync(document.Id, OrganizationId, RequestFor(FieldName.SupplierName, "NEW NAME"));

        var reloaded = await db.Documents.Include(d => d.ValidationWarnings).SingleAsync(d => d.Id == document.Id);
        Assert.Contains(reloaded.ValidationWarnings, w =>
            w.FieldName == nameof(FieldName.InvoiceNumber) && w.WarningCode == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public async Task UpdateFieldsAsync_ProvidesPreviouslyMissingRequiredField_RemovesRequiredFieldWarning()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.ValidationWarnings.Add(Warning(doc.Id, FieldName.InvoiceNumber, "REQUIRED_FIELD_MISSING", ValidationSeverity.High));
        });
        var sut = CreateSut(db);

        await sut.UpdateFieldsAsync(document.Id, OrganizationId, RequestFor(FieldName.InvoiceNumber, "INV-001"));

        var reloaded = await db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Contains(reloaded.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber) && f.NormalizedValue == "INV-001");
        Assert.DoesNotContain(reloaded.ValidationWarnings, w =>
            w.FieldName == nameof(FieldName.InvoiceNumber) && w.WarningCode == "REQUIRED_FIELD_MISSING");
    }

    [Fact]
    public async Task UpdateFieldsAsync_AfterSaving_StatusIsReviewed()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);

        await sut.UpdateFieldsAsync(document.Id, OrganizationId, RequestFor(FieldName.Notes, "ok"));

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(DocumentStatus.Reviewed, reloaded.Status);
    }

    [Fact]
    public async Task UpdateFieldsAsync_NewProfileOnlyFieldKey_CreatesAndPersistsField()
    {
        // "BuyerName" is a dynamic review-profile field key (see IDocumentProfileCatalog) with
        // no extractor yet — the user must still be able to fill it in manually and save it.
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);
        var request = new UpdateFieldsRequest
        {
            Fields = [new FieldUpdateItem { FieldName = "BuyerName", NormalizedValue = "Acme Corp" }]
        };

        await sut.UpdateFieldsAsync(document.Id, OrganizationId, request);

        var reloaded = await db.Documents.Include(d => d.Fields).SingleAsync(d => d.Id == document.Id);
        Assert.Contains(reloaded.Fields, f => f.FieldName == "BuyerName" && f.NormalizedValue == "Acme Corp" && f.IsEditedByUser);
    }

    [Fact]
    public async Task UpdateFieldsAsync_EmptyFieldName_ThrowsArgumentExceptionWithoutPersistingField()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);
        var request = new UpdateFieldsRequest
        {
            Fields = [new FieldUpdateItem { FieldName = "", NormalizedValue = "x" }]
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.UpdateFieldsAsync(document.Id, OrganizationId, request));

        var reloaded = await db.Documents.Include(d => d.Fields).SingleAsync(d => d.Id == document.Id);
        Assert.Empty(reloaded.Fields);
    }

    [Fact]
    public async Task UploadAsync_ValidFile_SavesToStorageAndCreatesUploadedDocument()
    {
        await using var db = CreateDbContext();
        var storage = new RecordingDocumentStorageService { StoredPathToReturn = "2026/07/abc123.pdf" };
        var sut = CreateSut(db, storage);
        using var fileStream = new MemoryStream([1, 2, 3]);

        var dto = await sut.UploadAsync(fileStream, "invoice.pdf", "application/pdf", 3, OrganizationId);

        Assert.Equal("invoice.pdf", storage.SavedFileName);
        Assert.Equal("application/pdf", storage.SavedContentType);
        Assert.Equal(DocumentStatus.Uploaded, dto.Status);
        Assert.Equal("invoice.pdf", dto.OriginalFileName);

        var stored = await db.Documents.SingleAsync(d => d.Id == dto.Id);
        Assert.Equal(OrganizationId, stored.OrganizationId);
        Assert.Equal("2026/07/abc123.pdf", stored.StoredFilePath);
        Assert.Equal(DocumentStatus.Uploaded, stored.Status);
    }

    [Fact]
    public async Task GetAllAsync_DocumentsFromAnotherOrganization_AreExcluded()
    {
        await using var db = CreateDbContext();
        var otherOrganizationId = Guid.NewGuid();
        var ownDocument = await SeedDocumentAsync(db, _ => { });
        var otherDocument = await SeedDocumentAsync(db, _ => { }, organizationId: otherOrganizationId);
        var sut = CreateSut(db);

        var results = await sut.GetAllAsync(OrganizationId);

        var result = Assert.Single(results);
        Assert.Equal(ownDocument.Id, result.Id);
        Assert.DoesNotContain(results, r => r.Id == otherDocument.Id);
    }

    [Fact]
    public async Task GetByIdAsync_DocumentBelongsToAnotherOrganization_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var otherOrganizationId = Guid.NewGuid();
        var document = await SeedDocumentAsync(db, _ => { }, organizationId: otherOrganizationId);
        var sut = CreateSut(db);

        var result = await sut.GetByIdAsync(document.Id, OrganizationId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingDocument_IncludesFieldsAndWarnings()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.Fields.Add(Field(doc.Id, FieldName.InvoiceNumber, "INV-1"));
            doc.ValidationWarnings.Add(Warning(doc.Id, FieldName.InvoiceNumber, "REQUIRED_FIELD_MISSING", ValidationSeverity.High));
        });
        var sut = CreateSut(db);

        var result = await sut.GetByIdAsync(document.Id, OrganizationId);

        Assert.NotNull(result);
        Assert.Single(result!.Fields);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task GetReviewByIdAsync_ExistingDocument_ReturnsDynamicReviewResponse()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.DocumentType = DocumentType.VatInvoice;
            doc.Fields.Add(Field(doc.Id, FieldName.DocumentType, nameof(DocumentType.VatInvoice)));
            doc.Fields.Add(Field(doc.Id, FieldName.SupplierName, "CONG TY ABC"));
        });
        var sut = CreateSut(db);

        var result = await sut.GetReviewByIdAsync(document.Id, OrganizationId);

        Assert.NotNull(result);
        Assert.Equal(DocumentCategory.VatInvoice, result!.DocumentCategory);
        Assert.Contains(result.Sections, s => s.SectionKey == "seller");
    }

    [Fact]
    public async Task GetOcrDebugAsync_DocumentWithStoredTables_IncludesTableCountAndCells()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.TablesJson = JsonSerializer.Serialize(new List<OcrTable>
            {
                new()
                {
                    RowCount = 2,
                    ColumnCount = 2,
                    Cells =
                    [
                        new() { RowIndex = 0, ColumnIndex = 0, Text = "ITEMS", Kind = "columnHeader" },
                        new() { RowIndex = 0, ColumnIndex = 1, Text = "AMOUNT", Kind = "columnHeader" },
                        new() { RowIndex = 1, ColumnIndex = 0, Text = "Widget" },
                        new() { RowIndex = 1, ColumnIndex = 1, Text = "10.00" }
                    ]
                }
            });
        });
        var sut = CreateSut(db);

        var result = await sut.GetOcrDebugAsync(document.Id, OrganizationId, exposeRawJson: false);

        Assert.NotNull(result);
        var table = Assert.Single(result!.Tables);
        Assert.Equal(2, table.RowCount);
        Assert.Contains(table.Rows, r => r.Cells.Any(c => c.Text == "Widget"));
    }

    [Fact]
    public async Task GetOcrDebugAsync_DocumentWithNoTables_ReturnsEmptyTablesWithoutThrowing()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);

        var result = await sut.GetOcrDebugAsync(document.Id, OrganizationId, exposeRawJson: false);

        Assert.NotNull(result);
        Assert.Empty(result!.Tables);
    }

    [Fact]
    public async Task GetReviewByIdAsync_DocumentBelongsToAnotherOrganization_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var otherOrganizationId = Guid.NewGuid();
        var document = await SeedDocumentAsync(db, _ => { }, organizationId: otherOrganizationId);
        var sut = CreateSut(db);

        var result = await sut.GetReviewByIdAsync(document.Id, OrganizationId);

        Assert.Null(result);
    }

    [Fact]
    public async Task MarkUploadedForProcessingAsync_PreviouslyFailedDocument_ResetsErrorAndTimestamps()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = "OCR provider timed out.";
            doc.ProcessingStartedAt = DateTime.UtcNow.AddMinutes(-5);
            doc.ProcessingCompletedAt = DateTime.UtcNow.AddMinutes(-4);
        });
        var sut = CreateSut(db);

        await sut.MarkUploadedForProcessingAsync(document.Id, OrganizationId);

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(DocumentStatus.Uploaded, reloaded.Status);
        Assert.Null(reloaded.ErrorMessage);
        Assert.Null(reloaded.ProcessingStartedAt);
        Assert.Null(reloaded.ProcessingCompletedAt);
    }

    [Fact]
    public async Task MarkUploadedForProcessingAsync_UnknownDocument_ThrowsKeyNotFoundException()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.MarkUploadedForProcessingAsync(Guid.NewGuid(), OrganizationId));
    }

    [Fact]
    public async Task DownloadOriginalAsync_ExistingDocument_ReturnsStreamFromStorage()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, doc => doc.StoredFilePath = "2026/07/invoice.pdf");
        var expectedStream = new MemoryStream([9, 9, 9]);
        var storage = new RecordingDocumentStorageService { StreamToReturn = expectedStream };
        var sut = CreateSut(db, storage);

        var (stream, contentType, fileName) = await sut.DownloadOriginalAsync(document.Id, OrganizationId);

        Assert.Same(expectedStream, stream);
        Assert.Equal("2026/07/invoice.pdf", storage.RequestedStreamPath);
        Assert.Equal("application/pdf", contentType);
        Assert.Equal("invoice.pdf", fileName);
    }

    [Fact]
    public async Task DownloadOriginalAsync_DocumentBelongsToAnotherOrganization_ThrowsKeyNotFoundException()
    {
        await using var db = CreateDbContext();
        var otherOrganizationId = Guid.NewGuid();
        var document = await SeedDocumentAsync(db, _ => { }, organizationId: otherOrganizationId);
        var sut = CreateSut(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.DownloadOriginalAsync(document.Id, OrganizationId));
    }

    private static UpdateFieldsRequest RequestFor(FieldName fieldName, string normalizedValue) => new()
    {
        Fields = [new FieldUpdateItem { FieldName = fieldName.ToString(), NormalizedValue = normalizedValue }]
    };

    private static DocumentService CreateSut(ApplicationDbContext db) =>
        CreateSut(db, new NoOpDocumentStorageService());

    private static DocumentService CreateSut(ApplicationDbContext db, IDocumentStorageService storage) =>
        new(
            db,
            storage,
            new FieldValidationService(new DocumentProfileCatalog()),
            new DocumentReviewMappingService(new DocumentProfileCatalog(), new ReviewTableBuilder()),
            new ReviewTableBuilder());

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<Document> SeedDocumentAsync(
        ApplicationDbContext db,
        Action<Document> configure,
        Guid? organizationId = null)
    {
        var resolvedOrganizationId = organizationId ?? OrganizationId;
        var organization = new Organization
        {
            Id = resolvedOrganizationId,
            Name = "Test Organization",
            Slug = $"test-organization-{Guid.NewGuid():N}"
        };

        var document = new Document
        {
            OrganizationId = resolvedOrganizationId,
            OriginalFileName = "invoice.pdf",
            StoredFilePath = "2026/07/invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Processed,
            DocumentType = DocumentType.Invoice
        };

        configure(document);

        db.Organizations.Add(organization);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return document;
    }

    private static ExtractedField Field(Guid documentId, FieldName fieldName, string value) => new()
    {
        DocumentId = documentId,
        FieldName = fieldName.ToString(),
        RawValue = value,
        NormalizedValue = value,
        Confidence = 0.95
    };

    private static ValidationWarning Warning(Guid documentId, FieldName fieldName, string code, ValidationSeverity severity) => new()
    {
        DocumentId = documentId,
        FieldName = fieldName.ToString(),
        WarningCode = code,
        Message = "Stale warning from the original OCR pass.",
        Severity = severity
    };

    private sealed class NoOpDocumentStorageService : IDocumentStorageService
    {
        public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by UpdateFieldsAsync.");

        public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by UpdateFieldsAsync.");

        public Task DeleteAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by UpdateFieldsAsync.");
    }

    private sealed class RecordingDocumentStorageService : IDocumentStorageService
    {
        public string StoredPathToReturn { get; set; } = "2026/07/stored-file.pdf";
        public Stream? StreamToReturn { get; set; }
        public string? SavedFileName { get; private set; }
        public string? SavedContentType { get; private set; }
        public string? RequestedStreamPath { get; private set; }

        public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default)
        {
            SavedFileName = originalFileName;
            SavedContentType = contentType;
            return Task.FromResult(StoredPathToReturn);
        }

        public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default)
        {
            RequestedStreamPath = storedPath;
            return Task.FromResult(StreamToReturn ?? new MemoryStream());
        }

        public Task DeleteAsync(string storedPath, CancellationToken ct = default) => Task.CompletedTask;
    }
}
