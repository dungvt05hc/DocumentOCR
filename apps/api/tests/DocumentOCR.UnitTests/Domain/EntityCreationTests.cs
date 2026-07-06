using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Xunit;

namespace DocumentOCR.UnitTests.Domain;

public class EntityCreationTests
{
    // ── Organization ──────────────────────────────────────────────────────────────

    [Fact]
    public void Organization_Create_HasDefaultGuidId()
    {
        var org = new Organization { Name = "Acme Corp", Slug = "acme-corp" };

        Assert.NotEqual(Guid.Empty, org.Id);
    }

    [Fact]
    public void Organization_Create_HasCreatedAndUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var org = new Organization { Name = "Acme Corp", Slug = "acme-corp" };
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(org.CreatedAt, before, after);
        Assert.InRange(org.UpdatedAt, before, after);
    }

    // ── Document ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Document_Create_DefaultStatusIsUploaded()
    {
        var doc = new Document
        {
            OrganizationId = Guid.NewGuid(),
            OriginalFileName = "invoice.pdf",
            StoredFilePath = "/storage/invoice.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 102400
        };

        Assert.Equal(DocumentStatus.Uploaded, doc.Status);
    }

    [Fact]
    public void Document_Create_DefaultDocumentTypeIsUnknown()
    {
        var doc = new Document { OriginalFileName = "scan.pdf" };

        Assert.Equal(DocumentType.Unknown, doc.DocumentType);
    }

    [Fact]
    public void Document_Create_HasNullProcessingTimestamps()
    {
        var doc = new Document { OriginalFileName = "scan.pdf" };

        Assert.Null(doc.ProcessingStartedAt);
        Assert.Null(doc.ProcessingCompletedAt);
        Assert.Null(doc.ErrorMessage);
    }

    [Fact]
    public void Document_Create_HasEmptyCollections()
    {
        var doc = new Document { OriginalFileName = "scan.pdf" };

        Assert.Empty(doc.Pages);
        Assert.Empty(doc.Fields);
        Assert.Empty(doc.ValidationWarnings);
        Assert.Empty(doc.OcrProviderLogs);
    }

    // ── DocumentPage ─────────────────────────────────────────────────────────────

    [Fact]
    public void DocumentPage_Create_StoresPageNumberAndRawText()
    {
        var docId = Guid.NewGuid();
        var page = new DocumentPage
        {
            DocumentId = docId,
            PageNumber = 1,
            RawText = "Invoice No: 12345"
        };

        Assert.Equal(docId, page.DocumentId);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("Invoice No: 12345", page.RawText);
    }

    // ── ExtractedField ────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractedField_Create_DefaultsAreCorrect()
    {
        var field = new ExtractedField
        {
            DocumentId = Guid.NewGuid(),
            FieldName = nameof(FieldName.TotalAmount),
            RawValue = "1.234.567",
            NormalizedValue = "1234567",
            Confidence = 0.97
        };

        Assert.False(field.IsEditedByUser);
        Assert.False(field.IsRequired);
        Assert.Null(field.EditedAt);
        Assert.Null(field.PageNumber);
        Assert.Null(field.BoundingBoxJson);
    }

    [Fact]
    public void ExtractedField_WhenEditedByUser_EditedAtIsSet()
    {
        var field = new ExtractedField
        {
            DocumentId = Guid.NewGuid(),
            FieldName = nameof(FieldName.SupplierName),
            NormalizedValue = "Original"
        };

        var editTime = DateTime.UtcNow;
        field.NormalizedValue = "Edited";
        field.IsEditedByUser = true;
        field.EditedAt = editTime;

        Assert.True(field.IsEditedByUser);
        Assert.Equal("Edited", field.NormalizedValue);
        Assert.Equal(editTime, field.EditedAt);
    }

    // ── ValidationWarning ────────────────────────────────────────────────────────

    [Fact]
    public void ValidationWarning_Create_DefaultSeverityIsWarning()
    {
        var warning = new ValidationWarning
        {
            DocumentId = Guid.NewGuid(),
            FieldName = nameof(FieldName.InvoiceNumber),
            WarningCode = "REQUIRED_FIELD_MISSING",
            Message = "Required field 'InvoiceNumber' is missing or empty."
        };

        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void ValidationWarning_Create_StoresAllProperties()
    {
        var docId = Guid.NewGuid();
        var warning = new ValidationWarning
        {
            DocumentId = docId,
            FieldName = nameof(FieldName.TotalAmount),
            WarningCode = "INVALID_TOTAL_AMOUNT",
            Message = "TotalAmount must be a positive number.",
            Severity = ValidationSeverity.Error
        };

        Assert.Equal(docId, warning.DocumentId);
        Assert.Equal(nameof(FieldName.TotalAmount), warning.FieldName);
        Assert.Equal("INVALID_TOTAL_AMOUNT", warning.WarningCode);
        Assert.Equal(ValidationSeverity.Error, warning.Severity);
    }

    // ── OcrProviderLog ────────────────────────────────────────────────────────────

    [Fact]
    public void OcrProviderLog_Create_StoresAllFields()
    {
        var docId = Guid.NewGuid();
        var log = new OcrProviderLog
        {
            DocumentId = docId,
            ProviderName = "AzureDocumentIntelligence",
            PageCount = 3,
            ProcessingTimeMs = 2500,
            EstimatedCost = 0.015m,
            Success = true
        };

        Assert.Equal(docId, log.DocumentId);
        Assert.Equal("AzureDocumentIntelligence", log.ProviderName);
        Assert.True(log.Success);
        Assert.Null(log.ErrorMessage);
    }
}
