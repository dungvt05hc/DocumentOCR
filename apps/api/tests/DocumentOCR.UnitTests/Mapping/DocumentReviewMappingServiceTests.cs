using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Profiles;
using Xunit;

namespace DocumentOCR.UnitTests.Mapping;

public class DocumentReviewMappingServiceTests
{
    private readonly DocumentReviewMappingService _sut = new(new DocumentProfileCatalog());

    [Fact]
    public void Map_VatInvoiceWithLegacySupplierName_PopulatesSellerNameViaAlias()
    {
        var document = NewDocument(DocumentType.VatInvoice);
        document.Fields.Add(Field(document.Id, nameof(FieldName.SupplierName), "CONG TY ABC"));

        var response = _sut.Map(document);

        var sellerSection = Assert.Single(response.Sections, s => s.SectionKey == "seller");
        var sellerName = Assert.Single(sellerSection.Fields, f => f.FieldKey == "SellerName");
        Assert.False(sellerName.IsMissing);
        Assert.Equal("CONG TY ABC", sellerName.Value);
    }

    [Fact]
    public void Map_RequiredFieldWithNoExtractedData_IsMarkedMissingAndCarriesItsWarning()
    {
        var document = NewDocument(DocumentType.VatInvoice);
        document.ValidationWarnings.Add(Warning(document.Id, nameof(FieldName.SupplierTaxCode), "REQUIRED_FIELD_MISSING", ValidationSeverity.High));

        var response = _sut.Map(document);

        var sellerSection = Assert.Single(response.Sections, s => s.SectionKey == "seller");
        var sellerTaxCode = Assert.Single(sellerSection.Fields, f => f.FieldKey == "SellerTaxCode");
        Assert.True(sellerTaxCode.IsMissing);
        Assert.True(sellerTaxCode.IsRequired);
        Assert.Contains("REQUIRED_FIELD_MISSING", sellerTaxCode.WarningCodes);
    }

    [Fact]
    public void Map_ExtractedFieldNotInProfile_LandsInOtherDetectedFieldsSection()
    {
        var document = NewDocument(DocumentType.VatInvoice);
        document.Fields.Add(Field(document.Id, "SomeUnmappedProviderField", "mystery value"));

        var response = _sut.Map(document);

        var otherSection = Assert.Single(response.Sections, s => s.SectionKey == "other");
        var otherField = Assert.Single(otherSection.Fields);
        Assert.Equal("SomeUnmappedProviderField", otherField.FieldKey);
        Assert.Equal("mystery value", otherField.Value);
    }

    [Fact]
    public void Map_MultipleConfidentFields_OverallConfidenceIsTheirAverage()
    {
        var document = NewDocument(DocumentType.VatInvoice);
        document.Fields.Add(Field(document.Id, nameof(FieldName.SupplierName), "X", confidence: 0.8));
        document.Fields.Add(Field(document.Id, nameof(FieldName.InvoiceNumber), "1", confidence: 0.6));

        var response = _sut.Map(document);

        Assert.NotNull(response.OverallConfidence);
        Assert.Equal(0.7, response.OverallConfidence!.Value, precision: 5);
    }

    [Fact]
    public void Map_NoConfidentFieldsAtAll_OverallConfidenceIsNull()
    {
        var document = NewDocument(DocumentType.VatInvoice);

        var response = _sut.Map(document);

        Assert.Null(response.OverallConfidence);
    }

    [Fact]
    public void Map_RestaurantBillCategory_AddsExperimentalInfoWarning()
    {
        var document = NewDocument(DocumentType.RestaurantBill);

        var response = _sut.Map(document);

        Assert.Contains(response.Warnings, w =>
            w.WarningCode == "EXPERIMENTAL_DOCUMENT_CATEGORY" && w.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public void Map_VatInvoiceCategory_DoesNotAddExperimentalWarning()
    {
        var document = NewDocument(DocumentType.VatInvoice);

        var response = _sut.Map(document);

        Assert.DoesNotContain(response.Warnings, w => w.WarningCode == "EXPERIMENTAL_DOCUMENT_CATEGORY");
    }

    [Fact]
    public void Map_PosReceiptCategory_ResolvesReceiptSectionsNotVatInvoiceSections()
    {
        var document = NewDocument(DocumentType.PosReceipt);

        var response = _sut.Map(document);

        Assert.Equal(DocumentCategory.PosReceipt, response.DocumentCategory);
        Assert.Contains(response.Sections, s => s.SectionKey == "merchant");
        Assert.DoesNotContain(response.Sections, s => s.SectionKey == "seller");
    }

    private static Document NewDocument(DocumentType documentType)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            OriginalFileName = "invoice.pdf",
            ContentType = "application/pdf",
            Status = DocumentStatus.Processed,
            DocumentType = documentType
        };

        document.Fields.Add(Field(document.Id, nameof(FieldName.DocumentType), documentType.ToString()));
        return document;
    }

    private static ExtractedField Field(Guid documentId, string fieldName, string value, double? confidence = 0.95) => new()
    {
        DocumentId = documentId,
        FieldName = fieldName,
        RawValue = value,
        NormalizedValue = value,
        Confidence = confidence
    };

    private static ValidationWarning Warning(Guid documentId, string fieldName, string code, ValidationSeverity severity) => new()
    {
        DocumentId = documentId,
        FieldName = fieldName,
        WarningCode = code,
        Message = "Test warning.",
        Severity = severity
    };
}
