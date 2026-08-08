using DocumentOCR.Domain.Enums;
using DocumentOCR.Application.Profiles;
using Xunit;

namespace DocumentOCR.UnitTests.Profiles;

public class DocumentProfileCatalogTests
{
    private readonly DocumentProfileCatalog _sut = new();

    [Theory]
    [InlineData(DocumentCategory.Unknown)]
    [InlineData(DocumentCategory.VatInvoice)]
    [InlineData(DocumentCategory.SalesReceipt)]
    [InlineData(DocumentCategory.PosReceipt)]
    [InlineData(DocumentCategory.RestaurantBill)]
    [InlineData(DocumentCategory.AppReceiptScreenshot)]
    [InlineData(DocumentCategory.InternationalInvoice)]
    [InlineData(DocumentCategory.CommercialInvoice)]
    public void GetProfile_EveryCategory_ReturnsNonEmptyProfile(DocumentCategory category)
    {
        var profile = _sut.GetProfile(category);

        Assert.NotEmpty(profile.Sections);
        Assert.All(profile.Sections, section => Assert.NotEmpty(section.Fields));
    }

    [Fact]
    public void GetProfile_SalesReceiptAndPosReceipt_ShareTheSameProfile()
    {
        Assert.Same(_sut.GetProfile(DocumentCategory.PosReceipt), _sut.GetProfile(DocumentCategory.SalesReceipt));
    }

    [Fact]
    public void GetProfile_CommercialInvoiceAndInternationalInvoice_ShareTheSameProfile()
    {
        Assert.Same(_sut.GetProfile(DocumentCategory.InternationalInvoice), _sut.GetProfile(DocumentCategory.CommercialInvoice));
    }

    [Fact]
    public void GetProfile_RestaurantBill_IsMarkedExperimental()
    {
        Assert.True(_sut.GetProfile(DocumentCategory.RestaurantBill).IsExperimental);
    }

    [Fact]
    public void GetProfile_VatInvoice_IsNotMarkedExperimental()
    {
        Assert.False(_sut.GetProfile(DocumentCategory.VatInvoice).IsExperimental);
    }

    [Fact]
    public void GetProfile_VatInvoice_HasExactlyFourVietnameseLayoutClusters()
    {
        var profile = _sut.GetProfile(DocumentCategory.VatInvoice);

        Assert.Equal(
            ["invoice", "seller", "buyer", "amounts"],
            profile.Sections.OrderBy(s => s.DisplayOrder).Select(s => s.SectionKey));
        Assert.Equal(
            ["Thông tin hoá đơn", "Người bán", "Người mua", "Số tiền"],
            profile.Sections.OrderBy(s => s.DisplayOrder).Select(s => s.Title));
    }

    [Fact]
    public void GetProfile_VatInvoice_DoesNotDefineAFlatVatRateField()
    {
        // VatRate moved to the per-line InvoiceTaxBreakdown table (see
        // DocumentReviewResponse.TaxBreakdown) — it must not also exist as a flat profile field.
        var profile = _sut.GetProfile(DocumentCategory.VatInvoice);

        Assert.DoesNotContain(profile.Sections.SelectMany(s => s.Fields), f => f.FieldKey == "VatRate");
    }

    [Theory]
    [InlineData("VatInvoice", DocumentCategory.VatInvoice)]
    [InlineData("AppReceiptScreenshot", DocumentCategory.AppReceiptScreenshot)]
    [InlineData("InternationalInvoice", DocumentCategory.InternationalInvoice)]
    [InlineData("CommercialInvoice", DocumentCategory.CommercialInvoice)]
    public void ResolveCategory_DetectedValueParsesDirectlyAsCategory_UsesThatCategory(string detected, DocumentCategory expected)
    {
        var result = _sut.ResolveCategory(detected, DocumentType.Unknown);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(DocumentType.Receipt, DocumentCategory.PosReceipt)]
    [InlineData(DocumentType.PosReceipt, DocumentCategory.PosReceipt)]
    [InlineData(DocumentType.RestaurantBill, DocumentCategory.RestaurantBill)]
    [InlineData(DocumentType.VatInvoice, DocumentCategory.VatInvoice)]
    [InlineData(DocumentType.ExpenseDocument, DocumentCategory.Unknown)]
    [InlineData(DocumentType.Unknown, DocumentCategory.Unknown)]
    public void ResolveCategory_UnparseableDetectedValue_FallsBackThroughLegacyDocumentType(
        DocumentType fallback, DocumentCategory expected)
    {
        // "Receipt"/"VatInvoice" etc. either don't exist on DocumentCategory at all (e.g.
        // "Receipt") or are handled by the direct-parse test above — this exercises the
        // fallback table for values not directly parseable as DocumentCategory.
        var result = _sut.ResolveCategory(fallback.ToString(), fallback);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveCategory_LegacyGenericInvoiceFallback_ResolvesToUnknownStrictProfile()
    {
        // DocumentType.Invoice is the "generic, more specific category couldn't be determined"
        // bucket (and the default when no "DocumentType" field was extracted at all) — it must
        // map to the Unknown category's strict fallback profile, not the VatInvoice profile,
        // to preserve today's exact validation behavior (tax code stays required, and required-
        // field warnings stay keyed by the legacy SupplierName/SupplierTaxCode/... names).
        var result = _sut.ResolveCategory("Invoice", DocumentType.Invoice);

        Assert.Equal(DocumentCategory.Unknown, result);
    }

    [Fact]
    public void ResolveCategory_NoDetectedValueAtAll_DefaultsToUnknownStrictProfile()
    {
        var result = _sut.ResolveCategory(null, DocumentType.Invoice);

        Assert.Equal(DocumentCategory.Unknown, result);
    }
}
