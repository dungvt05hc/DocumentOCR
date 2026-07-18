using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class ContentTypeMapperTests
{
    [Theory]
    [InlineData("invoice.pdf", "application/pdf")]
    [InlineData("invoice.PDF", "application/pdf")]
    [InlineData("receipt.jpg", "image/jpeg")]
    [InlineData("receipt.jpeg", "image/jpeg")]
    [InlineData("scan.png", "image/png")]
    public void TryGetContentType_SupportedExtension_ReturnsExpectedContentType(string fileName, string expected)
    {
        Assert.Equal(expected, ContentTypeMapper.TryGetContentType(fileName));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("archive.zip")]
    [InlineData("no-extension")]
    public void TryGetContentType_UnsupportedExtension_ReturnsNull(string fileName)
    {
        Assert.Null(ContentTypeMapper.TryGetContentType(fileName));
    }
}
