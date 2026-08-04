using DocumentOCR.Application.Credits;

namespace DocumentOCR.UnitTests.Credits;

public class CreditPricingTests
{
    private static readonly CreditOptions Options = new() { XmlParse = 1, OcrExtraction = 2 };

    [Theory]
    [InlineData("text/xml")]
    [InlineData("application/xml")]
    [InlineData("TEXT/XML")]
    public void ResolveCost_XmlContentType_ReturnsXmlParsePrice(string contentType)
    {
        Assert.Equal(1, CreditPricing.ResolveCost(contentType, Options));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public void ResolveCost_NonXmlContentType_ReturnsOcrExtractionPrice(string contentType)
    {
        Assert.Equal(2, CreditPricing.ResolveCost(contentType, Options));
    }
}
