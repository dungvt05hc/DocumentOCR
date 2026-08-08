using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentOCR.UnitTests.ClientProfiles;

public class ClientAutoSuggestServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public async Task TrySuggestAndAssignAsync_SupplierTaxCodeMatchesActiveClient_AssignsClient()
    {
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "0100109106")));
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.True(assigned);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(client.Id, reloaded.ClientProfileId);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_TaxCodeFormattedDifferently_StillMatchesAfterDigitNormalization()
    {
        // ClientProfile.TaxCode is stored digits-only (see ClientProfileService), and the
        // extracted field's RawValue may still carry punctuation if NormalizedValue wasn't set —
        // both sides must normalize the same way for the match to work.
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
        {
            var field = Field(doc.Id, "SupplierTaxCode", "0100109106");
            field.NormalizedValue = null;
            field.RawValue = "0100109106";
            doc.Fields.Add(field);
        });
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.True(assigned);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(client.Id, reloaded.ClientProfileId);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_NoMatchingClient_DoesNotAssign()
    {
        await using var db = CreateDbContext();
        await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "9999999999")));
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.False(assigned);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Null(reloaded.ClientProfileId);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_MatchingClientIsInactive_DoesNotAssign()
    {
        await using var db = CreateDbContext();
        await SeedClientAsync(db, taxCode: "0100109106", isActive: false);
        var document = await SeedDocumentAsync(db, doc =>
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "0100109106")));
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.False(assigned);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_DocumentAlreadyHasClientAssigned_DoesNotOverwrite()
    {
        await using var db = CreateDbContext();
        var originalClient = await SeedClientAsync(db, taxCode: "1111111111");
        var betterMatchClient = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.ClientProfileId = originalClient.Id;
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "0100109106"));
        });
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.False(assigned);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(originalClient.Id, reloaded.ClientProfileId);
        Assert.NotEqual(betterMatchClient.Id, reloaded.ClientProfileId);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_NoSupplierTaxCodeField_DoesNotAssign()
    {
        await using var db = CreateDbContext();
        await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.False(assigned);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_ClientBelongsToDifferentOrganization_DoesNotAssign()
    {
        await using var db = CreateDbContext();
        await SeedClientAsync(db, taxCode: "0100109106", organizationId: Guid.NewGuid());
        var document = await SeedDocumentAsync(db, doc =>
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "0100109106")));
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.False(assigned);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_UnknownDocument_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(Guid.NewGuid());

        Assert.False(assigned);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_BuyerTaxCodeMatchesActiveClient_AssignsClient()
    {
        // Closes the "buyer-side matching" gap noted in docs/decisions.md — a purchase document
        // (hóa đơn đầu vào) where the client is the buyer, not the seller.
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
            doc.Fields.Add(Field(doc.Id, "BuyerTaxCode", "0100109106")));
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.True(assigned);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(client.Id, reloaded.ClientProfileId);
    }

    [Fact]
    public async Task TrySuggestAndAssignAsync_SupplierMatches_PreferredOverBuyerMatch()
    {
        await using var db = CreateDbContext();
        var sellerClient = await SeedClientAsync(db, taxCode: "0100109106");
        await SeedClientAsync(db, taxCode: "0109876543");
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "0100109106"));
            doc.Fields.Add(Field(doc.Id, "BuyerTaxCode", "0109876543"));
        });
        var sut = CreateSut(db);

        var assigned = await sut.TrySuggestAndAssignAsync(document.Id);

        Assert.True(assigned);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(sellerClient.Id, reloaded.ClientProfileId);
    }

    // ── InferDirectionAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task InferDirectionAsync_BuyerTaxCodeMatchesClient_ResolvesPurchase()
    {
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.ClientProfileId = client.Id;
            doc.Fields.Add(Field(doc.Id, "BuyerTaxCode", "0100109106"));
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "9999999999"));
        });
        var sut = CreateSut(db);

        var direction = await sut.InferDirectionAsync(document.Id);

        Assert.Equal(DocumentDirection.Purchase, direction);
        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(DocumentDirection.Purchase, reloaded.Direction);
    }

    [Fact]
    public async Task InferDirectionAsync_SupplierTaxCodeMatchesClient_ResolvesSale()
    {
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.ClientProfileId = client.Id;
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "0100109106"));
        });
        var sut = CreateSut(db);

        var direction = await sut.InferDirectionAsync(document.Id);

        Assert.Equal(DocumentDirection.Sale, direction);
    }

    [Fact]
    public async Task InferDirectionAsync_NoClientAssigned_ResolvesUnknownAndAddsWarning()
    {
        await using var db = CreateDbContext();
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);

        var direction = await sut.InferDirectionAsync(document.Id);

        Assert.Equal(DocumentDirection.Unknown, direction);
        var warnings = await db.ValidationWarnings.Where(w => w.DocumentId == document.Id).ToListAsync();
        Assert.Contains(warnings, w => w.WarningCode == "DOCUMENT_DIRECTION_UNKNOWN");
    }

    [Fact]
    public async Task InferDirectionAsync_NeitherTaxCodeMatchesClient_ResolvesUnknownAndAddsWarning()
    {
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, doc =>
        {
            doc.ClientProfileId = client.Id;
            doc.Fields.Add(Field(doc.Id, "SupplierTaxCode", "1111111111"));
            doc.Fields.Add(Field(doc.Id, "BuyerTaxCode", "2222222222"));
        });
        var sut = CreateSut(db);

        var direction = await sut.InferDirectionAsync(document.Id);

        Assert.Equal(DocumentDirection.Unknown, direction);
        var warnings = await db.ValidationWarnings.Where(w => w.DocumentId == document.Id).ToListAsync();
        Assert.Contains(warnings, w => w.WarningCode == "DOCUMENT_DIRECTION_UNKNOWN");
    }

    [Fact]
    public async Task InferDirectionAsync_CalledAgainAfterResolving_RemovesStaleUnknownWarning()
    {
        await using var db = CreateDbContext();
        var client = await SeedClientAsync(db, taxCode: "0100109106");
        var document = await SeedDocumentAsync(db, _ => { });
        var sut = CreateSut(db);

        await sut.InferDirectionAsync(document.Id);

        var reloaded = await db.Documents.Include(d => d.Fields).SingleAsync(d => d.Id == document.Id);
        reloaded.ClientProfileId = client.Id;
        db.ExtractedFields.Add(Field(document.Id, "SupplierTaxCode", "0100109106"));
        await db.SaveChangesAsync();

        var direction = await sut.InferDirectionAsync(document.Id);

        Assert.Equal(DocumentDirection.Sale, direction);
        var warnings = await db.ValidationWarnings.Where(w => w.DocumentId == document.Id).ToListAsync();
        Assert.DoesNotContain(warnings, w => w.WarningCode == "DOCUMENT_DIRECTION_UNKNOWN");
    }

    private static ClientAutoSuggestService CreateSut(ApplicationDbContext db) =>
        new(db, new FieldNormalizationService());

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<ClientProfile> SeedClientAsync(
        ApplicationDbContext db, string taxCode, bool isActive = true, Guid? organizationId = null)
    {
        var resolvedOrganizationId = organizationId ?? OrganizationId;
        await EnsureOrganizationAsync(db, resolvedOrganizationId);

        var client = new ClientProfile
        {
            OrganizationId = resolvedOrganizationId,
            Name = "Test Client",
            TaxCode = taxCode,
            ClientType = ClientType.HouseholdBusiness,
            IsActive = isActive
        };

        db.ClientProfiles.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private static async Task<Document> SeedDocumentAsync(
        ApplicationDbContext db, Action<Document> configure, Guid? organizationId = null)
    {
        var resolvedOrganizationId = organizationId ?? OrganizationId;
        await EnsureOrganizationAsync(db, resolvedOrganizationId);

        var document = new Document
        {
            OrganizationId = resolvedOrganizationId,
            OriginalFileName = "invoice.pdf",
            StoredFilePath = "2026/07/invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Processed,
            DocumentType = DocumentType.VatInvoice
        };

        configure(document);

        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private static async Task EnsureOrganizationAsync(ApplicationDbContext db, Guid organizationId)
    {
        if (await db.Organizations.AnyAsync(o => o.Id == organizationId)) return;

        db.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "Test Organization",
            Slug = $"test-organization-{Guid.NewGuid():N}"
        });
        await db.SaveChangesAsync();
    }

    private static ExtractedField Field(Guid documentId, string fieldName, string value) => new()
    {
        DocumentId = documentId,
        FieldName = fieldName,
        RawValue = value,
        NormalizedValue = value,
        Confidence = 0.95
    };
}
