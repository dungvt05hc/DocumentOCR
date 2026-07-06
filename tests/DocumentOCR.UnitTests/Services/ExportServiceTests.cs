using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentOCR.UnitTests.Services;

public class ExportServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task ExportToExcelAsync_DocumentsBelongToOrganization_ReturnsExcelBytesAndFileName()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, OrganizationId);
        var excel = new RecordingExcelExportService { BytesToReturn = [1, 2, 3] };
        var sut = new ExportService(db, excel);

        var (bytes, fileName) = await sut.ExportToExcelAsync([document.Id], OrganizationId);

        Assert.Equal([document.Id], excel.RequestedDocumentIds);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.StartsWith("DocumentOCR_Export_", fileName);
        Assert.EndsWith(".xlsx", fileName);
    }

    [Fact]
    public async Task ExportToExcelAsync_UnknownDocumentId_ThrowsKeyNotFoundExceptionAndDoesNotCallExcelService()
    {
        await using var db = CreateDbContext();
        var excel = new RecordingExcelExportService();
        var sut = new ExportService(db, excel);
        var unknownId = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.ExportToExcelAsync([unknownId], OrganizationId));

        Assert.Null(excel.RequestedDocumentIds);
    }

    [Fact]
    public async Task ExportToExcelAsync_DocumentBelongsToAnotherOrganization_TreatsItAsMissing()
    {
        await using var db = CreateDbContext();
        var otherOrganizationId = Guid.NewGuid();
        var document = await SeedDocumentAsync(db, otherOrganizationId);
        var excel = new RecordingExcelExportService();
        var sut = new ExportService(db, excel);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.ExportToExcelAsync([document.Id], OrganizationId));

        Assert.Contains(document.Id.ToString(), exception.Message);
    }

    [Fact]
    public async Task ExportToExcelAsync_MixOfOwnedAndUnknownIds_ThrowsBeforeCallingExcelService()
    {
        await using var db = CreateDbContext();
        var ownedDocument = await SeedDocumentAsync(db, OrganizationId);
        var excel = new RecordingExcelExportService();
        var sut = new ExportService(db, excel);
        var unknownId = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.ExportToExcelAsync([ownedDocument.Id, unknownId], OrganizationId));

        Assert.Null(excel.RequestedDocumentIds);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<Document> SeedDocumentAsync(ApplicationDbContext db, Guid organizationId)
    {
        var organization = new Organization
        {
            Id = organizationId,
            Name = "Test Organization",
            Slug = $"test-organization-{Guid.NewGuid():N}"
        };

        var document = new Document
        {
            OrganizationId = organizationId,
            OriginalFileName = "invoice.pdf",
            StoredFilePath = "2026/07/invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Reviewed,
            DocumentType = DocumentType.Invoice
        };

        db.Organizations.Add(organization);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return document;
    }

    private sealed class RecordingExcelExportService : IExcelExportService
    {
        public byte[] BytesToReturn { get; set; } = [];
        public List<Guid>? RequestedDocumentIds { get; private set; }

        public Task<byte[]> ExportAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default)
        {
            RequestedDocumentIds = documentIds.ToList();
            return Task.FromResult(BytesToReturn);
        }
    }
}
