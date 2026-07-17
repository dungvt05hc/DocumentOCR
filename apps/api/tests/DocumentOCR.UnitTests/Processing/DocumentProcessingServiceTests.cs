using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocumentOCR.UnitTests.Processing;

public class DocumentProcessingServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_UnknownDocument_ReturnsWithoutThrowing()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db, new FakeOcrProvider(), new FakeDocumentStorageService());

        await sut.ProcessAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulOcr_MarksDocumentProcessedWithExtractedFieldsAndPages()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var sut = CreateSut(db, new FakeOcrProvider(), new FakeDocumentStorageService());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents
            .Include(d => d.Pages)
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLogs)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.Processed, reloaded.Status);
        Assert.Equal(1, reloaded.PageCount);
        Assert.NotNull(reloaded.ProcessingCompletedAt);
        Assert.Null(reloaded.ErrorMessage);
        Assert.Single(reloaded.Pages);

        Assert.Contains(reloaded.Fields, f => f.FieldName == nameof(FieldName.SupplierTaxCode) && f.NormalizedValue == "0100109106");
        Assert.Contains(reloaded.Fields, f => f.FieldName == nameof(FieldName.TotalAmount) && f.NormalizedValue == "1236767");
        Assert.Empty(reloaded.ValidationWarnings);

        var log = Assert.Single(reloaded.OcrProviderLogs);
        Assert.Equal("Fake", log.ProviderName);
        Assert.Equal("Fake", log.ModelId);
        Assert.True(log.Success);
    }

    [Fact]
    public async Task ProcessAsync_ReprocessingDocument_RemovesStaleFieldsAndPagesButKeepsOcrProviderLogHistory()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Processed, doc =>
        {
            doc.Pages.Add(new DocumentPage { DocumentId = doc.Id, PageNumber = 1, RawText = "stale page text" });
            doc.Fields.Add(new ExtractedField { DocumentId = doc.Id, FieldName = nameof(FieldName.Notes), RawValue = "stale note", NormalizedValue = "stale note" });
            doc.ValidationWarnings.Add(new ValidationWarning { DocumentId = doc.Id, FieldName = nameof(FieldName.Notes), WarningCode = "STALE", Severity = ValidationSeverity.Info, Message = "stale warning" });
            doc.OcrProviderLogs.Add(new OcrProviderLog { DocumentId = doc.Id, ProviderName = "StaleProvider", Success = true });
        });
        var sut = CreateSut(db, new FakeOcrProvider(), new FakeDocumentStorageService());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents
            .Include(d => d.Pages)
            .Include(d => d.Fields)
            .Include(d => d.ValidationWarnings)
            .Include(d => d.OcrProviderLogs)
            .SingleAsync(d => d.Id == document.Id);

        Assert.DoesNotContain(reloaded.Fields, f => f.NormalizedValue == "stale note");
        Assert.DoesNotContain(reloaded.ValidationWarnings, w => w.WarningCode == "STALE");
        Assert.Single(reloaded.Pages);
        Assert.NotEqual("stale page text", reloaded.Pages.Single().RawText);

        // OcrProviderLog is an append-only audit trail: the prior attempt's row is preserved
        // alongside the new one rather than being replaced.
        Assert.Equal(2, reloaded.OcrProviderLogs.Count);
        Assert.Contains(reloaded.OcrProviderLogs, l => l.ProviderName == "StaleProvider");
        Assert.Contains(reloaded.OcrProviderLogs, l => l.ProviderName == "Fake");
    }

    [Fact]
    public async Task ProcessAsync_OcrProviderReturnsUnsuccessfulResult_MarksDocumentFailedWithProviderMessage()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var failingProvider = new StubOcrProvider(new OcrResult { Success = false, ErrorMessage = "Provider quota exceeded.", PageCount = 0 });
        var sut = CreateSut(db, failingProvider, new FakeDocumentStorageService());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents.Include(d => d.OcrProviderLogs).SingleAsync(d => d.Id == document.Id);
        Assert.Equal(DocumentStatus.Failed, reloaded.Status);
        Assert.Equal("Provider quota exceeded.", reloaded.ErrorMessage);
        var log = Assert.Single(reloaded.OcrProviderLogs);
        Assert.False(log.Success);
    }

    [Fact]
    public async Task ProcessAsync_OcrProviderThrows_MarksDocumentFailedWithGenericMessage()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var throwingProvider = new ThrowingOcrProvider();
        var sut = CreateSut(db, throwingProvider, new FakeDocumentStorageService());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(DocumentStatus.Failed, reloaded.Status);
        Assert.Equal("Document processing failed unexpectedly. Please retry or contact support.", reloaded.ErrorMessage);
    }

    private static DocumentProcessingService CreateSut(
        ApplicationDbContext db,
        IDocumentOcrProvider ocrProvider,
        IDocumentStorageService storage) =>
        new(
            db,
            storage,
            ocrProvider,
            new FieldExtractionService(),
            new FieldNormalizationService(),
            new FieldValidationService(),
            NullLogger<DocumentProcessingService>.Instance);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<Document> SeedDocumentAsync(
        ApplicationDbContext db,
        DocumentStatus status,
        Action<Document>? configure = null)
    {
        var organization = new Organization
        {
            Id = OrganizationId,
            Name = "Test Organization",
            Slug = $"test-organization-{Guid.NewGuid():N}"
        };

        var document = new Document
        {
            OrganizationId = OrganizationId,
            OriginalFileName = "invoice.pdf",
            StoredFilePath = "2026/07/invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = status
        };

        configure?.Invoke(document);

        db.Organizations.Add(organization);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return document;
    }

    private sealed class FakeDocumentStorageService : IDocumentStorageService
    {
        public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by ProcessAsync.");

        public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream([0x25, 0x50, 0x44, 0x46]));

        public Task DeleteAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by ProcessAsync.");
    }

    private sealed class StubOcrProvider(OcrResult result) : IDocumentOcrProvider
    {
        public string ProviderName => "Stub";

        public Task<OcrResult> AnalyzeAsync(DocumentInput input, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingOcrProvider : IDocumentOcrProvider
    {
        public string ProviderName => "Throwing";

        public Task<OcrResult> AnalyzeAsync(DocumentInput input, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated OCR provider failure.");
    }
}
