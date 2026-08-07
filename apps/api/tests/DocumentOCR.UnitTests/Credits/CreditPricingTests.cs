using DocumentOCR.Application.Credits;

namespace DocumentOCR.UnitTests.Credits;

public class CreditPricingTests
{
    private static readonly CreditOptions Options = new() { XmlParse = 0, PdfTextLayer = 0, OcrExtraction = 2 };

    [Theory]
    [InlineData("text/xml")]
    [InlineData("application/xml")]
    [InlineData("TEXT/XML")]
    [InlineData("application/octet-stream")] // extension still identifies it as XML
    [InlineData("")]
    public void ResolveCost_XmlUpload_ReturnsXmlParsePrice(string contentType)
    {
        Assert.Equal(0, CreditPricing.ResolveCost(contentType, "invoice.xml", Options));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public void ResolveCost_NonXmlUpload_ReturnsOcrExtractionPrice(string contentType)
    {
        Assert.Equal(2, CreditPricing.ResolveCost(contentType, "invoice.pdf", Options));
    }

    [Fact]
    public void ResolveActualCost_StructuredXmlProvider_ReturnsXmlParsePrice()
    {
        Assert.Equal(0, CreditPricing.ResolveActualCost(CreditPricing.StructuredXmlProviderName, Options));
    }

    [Fact]
    public void ResolveActualCost_PdfTextLayerProvider_ReturnsPdfTextLayerPrice()
    {
        Assert.Equal(0, CreditPricing.ResolveActualCost(CreditPricing.PdfTextLayerProviderName, Options));
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("Azure")]
    [InlineData("Paddle")]
    public void ResolveActualCost_RealOcrProvider_ReturnsOcrExtractionPrice(string providerName)
    {
        Assert.Equal(2, CreditPricing.ResolveActualCost(providerName, Options));
    }
}
