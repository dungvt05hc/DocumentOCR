using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Profiles;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        Assert.Contains(reloaded.Fields, f => f.FieldName == nameof(FieldName.Currency) && f.NormalizedValue == "VND");
        var warning = Assert.Single(reloaded.ValidationWarnings);
        Assert.Equal(nameof(FieldName.Currency), warning.FieldName);
        Assert.Equal("LOW_CONFIDENCE", warning.WarningCode);

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
        var failingProvider = new StubOcrProvider(new NormalizedOcrDocument { Success = false, ProviderName = "Stub", ErrorMessage = "Provider quota exceeded.", PageCount = 0 });
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
        IDocumentStorageService storage,
        OcrOptions? ocrOptions = null,
        IStructuredInvoiceParser? structuredInvoiceParser = null,
        ICreditService? creditService = null,
        CreditOptions? creditOptions = null) =>
        new(
            db,
            storage,
            ocrProvider,
            new FieldExtractionService(),
            new FieldNormalizationService(),
            new FieldValidationService(new DocumentProfileCatalog()),
            structuredInvoiceParser ?? new NeverMatchingStructuredInvoiceParser(),
            creditService ?? new RecordingCreditService(),
            Options.Create(ocrOptions ?? new OcrOptions()),
            Options.Create(creditOptions ?? new CreditOptions()),
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
        // ProcessAsync also writes raw/normalized OCR artifacts via SaveAsync when
        // StoreRawProviderResponse/StoreNormalizedOcrResult are enabled (the default), so this
        // must return a value rather than throw.
        public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default) =>
            Task.FromResult($"fake/{originalFileName}");

        public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream([0x25, 0x50, 0x44, 0x46]));

        public Task DeleteAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by ProcessAsync.");
    }

    // ── StoreRawProviderResponse ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_StoreRawProviderResponseTrue_PersistsRawResponseJson()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var provider = new StubOcrProvider(new NormalizedOcrDocument
        {
            Success = true, ProviderName = "Stub", PageCount = 1, RawProviderResponseJson = "{\"raw\":true}"
        });
        var sut = CreateSut(db, provider, new FakeDocumentStorageService(), new OcrOptions { StoreRawProviderResponse = true });

        await sut.ProcessAsync(document.Id);

        var log = await db.OcrProviderLogs.SingleAsync(l => l.DocumentId == document.Id);
        Assert.Equal("{\"raw\":true}", log.RawResponseJson);
        Assert.NotNull(log.RawResponsePath);
    }

    [Fact]
    public async Task ProcessAsync_StoreRawProviderResponseFalse_DoesNotPersistRawResponseJson()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var provider = new StubOcrProvider(new NormalizedOcrDocument
        {
            Success = true, ProviderName = "Stub", PageCount = 1, RawProviderResponseJson = "{\"raw\":true}"
        });
        var sut = CreateSut(db, provider, new FakeDocumentStorageService(), new OcrOptions { StoreRawProviderResponse = false });

        await sut.ProcessAsync(document.Id);

        var log = await db.OcrProviderLogs.SingleAsync(l => l.DocumentId == document.Id);
        Assert.Null(log.RawResponseJson);
        Assert.Null(log.RawResponsePath);
    }

    // ── StoreNormalizedOcrResult ───────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_StoreNormalizedOcrResultTrue_PersistsNormalizedResultPath()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var provider = new StubOcrProvider(new NormalizedOcrDocument { Success = true, ProviderName = "Stub", PageCount = 1 });
        var sut = CreateSut(db, provider, new FakeDocumentStorageService(), new OcrOptions { StoreNormalizedOcrResult = true });

        await sut.ProcessAsync(document.Id);

        var log = await db.OcrProviderLogs.SingleAsync(l => l.DocumentId == document.Id);
        Assert.NotNull(log.NormalizedResultPath);
    }

    [Fact]
    public async Task ProcessAsync_StoreNormalizedOcrResultFalse_DoesNotPersistNormalizedResultPath()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var provider = new StubOcrProvider(new NormalizedOcrDocument { Success = true, ProviderName = "Stub", PageCount = 1 });
        var sut = CreateSut(db, provider, new FakeDocumentStorageService(), new OcrOptions { StoreNormalizedOcrResult = false });

        await sut.ProcessAsync(document.Id);

        var log = await db.OcrProviderLogs.SingleAsync(l => l.DocumentId == document.Id);
        Assert.Null(log.NormalizedResultPath);
    }

    private sealed class StubOcrProvider(NormalizedOcrDocument result) : IDocumentOcrProvider
    {
        public string ProviderName => "Stub";

        public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingOcrProvider : IDocumentOcrProvider
    {
        public string ProviderName => "Throwing";

        public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated OCR provider failure.");
    }

    // ── Structured (TT78 XML) invoice fast path ─────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_XmlDocument_UsesStructuredParserAndNeverCallsOcrProvider()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded, doc =>
        {
            doc.OriginalFileName = "invoice.xml";
            doc.ContentType = "text/xml";
        });
        var ocrProvider = new RecordingOcrProvider();
        var sut = CreateSut(
            db,
            ocrProvider,
            new XmlFixtureDocumentStorageService(),
            structuredInvoiceParser: new TT78XmlInvoiceParser());

        await sut.ProcessAsync(document.Id);

        Assert.Equal(0, ocrProvider.CallCount);

        var reloaded = await db.Documents
            .Include(d => d.Pages)
            .Include(d => d.Fields)
            .Include(d => d.OcrProviderLogs)
            .SingleAsync(d => d.Id == document.Id);

        Assert.Equal(DocumentStatus.Processed, reloaded.Status);
        Assert.Equal(1, reloaded.PageCount);
        Assert.Empty(reloaded.Pages);
        Assert.Equal(DocumentType.VatInvoice, reloaded.DocumentType);
        Assert.Contains(reloaded.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber) && f.RawValue == "00001234");
        Assert.Contains(reloaded.Fields, f => f.FieldName == nameof(FieldName.TotalAmount) && f.NormalizedValue == "1236767");

        var log = Assert.Single(reloaded.OcrProviderLogs);
        Assert.Equal("TT78Xml", log.ProviderName);
        Assert.Equal(0m, log.EstimatedCost);
        Assert.True(log.Success);
    }

    [Fact]
    public async Task ProcessAsync_OcrTextWithRecognizedVatRateLine_SynthesizesOneTaxBreakdownRow()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var provider = new StubOcrProvider(new NormalizedOcrDocument
        {
            Success = true,
            ProviderName = "Stub",
            PageCount = 1,
            FullText = "Cộng tiền hàng: 1.000.000\nThuế GTGT (10%): 100.000\nTổng thanh toán: 1.100.000",
            Pages =
            [
                new OcrPage
                {
                    PageNumber = 1,
                    FullText = "Cộng tiền hàng: 1.000.000\nThuế GTGT (10%): 100.000\nTổng thanh toán: 1.100.000",
                    Lines =
                    [
                        new OcrLine { LineNumber = 1, PageNumber = 1, Text = "Cộng tiền hàng: 1.000.000", Confidence = 0.9 },
                        new OcrLine { LineNumber = 2, PageNumber = 1, Text = "Thuế GTGT (10%): 100.000", Confidence = 0.9 },
                        new OcrLine { LineNumber = 3, PageNumber = 1, Text = "Tổng thanh toán: 1.100.000", Confidence = 0.9 }
                    ]
                }
            ]
        });
        var sut = CreateSut(db, provider, new FakeDocumentStorageService());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents
            .Include(d => d.Fields)
            .Include(d => d.TaxBreakdowns)
            .SingleAsync(d => d.Id == document.Id);

        Assert.DoesNotContain(reloaded.Fields, f => f.FieldName == "VatRate");
        var row = Assert.Single(reloaded.TaxBreakdowns);
        Assert.Equal("10%", row.VatRate);
        Assert.Equal(1000000m, row.TaxableAmount);
        Assert.Equal(100000m, row.TaxAmount);
    }

    private sealed class RecordingOcrProvider : IDocumentOcrProvider
    {
        public int CallCount { get; private set; }

        public string ProviderName => "Recording";

        public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new NormalizedOcrDocument { Success = true, ProviderName = ProviderName, PageCount = 1 });
        }
    }

    [Fact]
    public async Task ProcessAsync_XmlDocumentWithTaxBreakdown_PersistsTaxBreakdownRows()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded, doc =>
        {
            doc.OriginalFileName = "invoice.xml";
            doc.ContentType = "text/xml";
        });
        var sut = CreateSut(
            db,
            new RecordingOcrProvider(),
            new XmlFixtureDocumentStorageService("1C26TNH_00000947_4500609710.xml"),
            structuredInvoiceParser: new TT78XmlInvoiceParser());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents.Include(d => d.TaxBreakdowns).SingleAsync(d => d.Id == document.Id);
        var row = Assert.Single(reloaded.TaxBreakdowns);
        Assert.Equal("10%", row.VatRate);
        Assert.Equal(10019909.000000m, row.TaxableAmount);
        Assert.Equal(1001991.000000m, row.TaxAmount);
    }

    [Fact]
    public async Task ProcessAsync_ReprocessingXmlDocument_ReplacesStaleTaxBreakdownRows()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Processed, doc =>
        {
            doc.OriginalFileName = "invoice.xml";
            doc.ContentType = "text/xml";
            doc.TaxBreakdowns.Add(new InvoiceTaxBreakdown { DocumentId = doc.Id, VatRate = "5%", TaxableAmount = 1, TaxAmount = 1, SortOrder = 0 });
        });
        var sut = CreateSut(
            db,
            new RecordingOcrProvider(),
            new XmlFixtureDocumentStorageService("1C26TNH_00000947_4500609710.xml"),
            structuredInvoiceParser: new TT78XmlInvoiceParser());

        await sut.ProcessAsync(document.Id);

        var reloaded = await db.Documents.Include(d => d.TaxBreakdowns).SingleAsync(d => d.Id == document.Id);
        var row = Assert.Single(reloaded.TaxBreakdowns);
        Assert.Equal("10%", row.VatRate);
    }

    private sealed class XmlFixtureDocumentStorageService(string fixtureFileName = "valid-invoice.xml") : IDocumentStorageService
    {
        public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default) =>
            Task.FromResult($"fake/{originalFileName}");

        public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default) =>
            Task.FromResult<Stream>(File.OpenRead(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "tt78", fixtureFileName)));

        public Task DeleteAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by ProcessAsync.");
    }

    private sealed class NeverMatchingStructuredInvoiceParser : IStructuredInvoiceParser
    {
        public bool CanParse(string contentType, string fileName) => false;

        public Task<StructuredInvoiceResult> ParseAsync(Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException("Should never be called when CanParse returns false.");
    }

    // ── Credit refund on permanent failure ──────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_OcrProviderReturnsUnsuccessfulResult_RefundsProcessingCredits()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded, doc => doc.ContentType = "application/pdf");
        var failingProvider = new StubOcrProvider(new NormalizedOcrDocument { Success = false, ProviderName = "Stub", ErrorMessage = "boom", PageCount = 0 });
        var creditService = new RecordingCreditService();
        var creditOptions = new CreditOptions { OcrExtraction = 2 };
        var sut = CreateSut(db, failingProvider, new FakeDocumentStorageService(), creditService: creditService, creditOptions: creditOptions);

        await sut.ProcessAsync(document.Id);

        var refund = Assert.Single(creditService.Refunds);
        Assert.Equal(OrganizationId, refund.OrganizationId);
        Assert.Equal(2, refund.Amount);
        Assert.Equal("Document", refund.ReferenceType);
        Assert.Equal(document.Id, refund.ReferenceId);
    }

    [Fact]
    public async Task ProcessAsync_OcrProviderThrows_RefundsProcessingCredits()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded, doc => doc.ContentType = "application/pdf");
        var creditService = new RecordingCreditService();
        var creditOptions = new CreditOptions { OcrExtraction = 2 };
        var sut = CreateSut(db, new ThrowingOcrProvider(), new FakeDocumentStorageService(), creditService: creditService, creditOptions: creditOptions);

        await sut.ProcessAsync(document.Id);

        var refund = Assert.Single(creditService.Refunds);
        Assert.Equal(2, refund.Amount);
        Assert.Equal(document.Id, refund.ReferenceId);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulOcr_DoesNotRefundCredits()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, DocumentStatus.Uploaded);
        var creditService = new RecordingCreditService();
        var sut = CreateSut(db, new FakeOcrProvider(), new FakeDocumentStorageService(), creditService: creditService);

        await sut.ProcessAsync(document.Id);

        Assert.Empty(creditService.Refunds);
    }

    private sealed class RecordingCreditService : ICreditService
    {
        public List<(Guid OrganizationId, int Amount, string ReferenceType, Guid ReferenceId)> Refunds { get; } = [];

        public Task<long> GetBalanceAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(0L);

        public Task<(IReadOnlyList<CreditTransaction> Items, int TotalCount)> GetTransactionsAsync(
            Guid organizationId, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<CreditTransaction> Items, int TotalCount)>(([], 0));

        public Task<bool> TryConsumeAsync(
            Guid organizationId, int amount, string referenceType, Guid referenceId,
            string? description = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task RefundAsync(
            Guid organizationId, int amount, string referenceType, Guid referenceId,
            string? description = null, CancellationToken ct = default)
        {
            Refunds.Add((organizationId, amount, referenceType, referenceId));
            return Task.CompletedTask;
        }

        public Task TopUpAsync(Guid organizationId, int amount, string? description = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
