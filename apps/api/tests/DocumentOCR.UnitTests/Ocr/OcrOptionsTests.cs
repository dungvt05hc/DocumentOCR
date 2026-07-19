using DocumentOCR.Infrastructure.Ocr;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

public class OcrOptionsTests
{
    [Fact]
    public void Defaults_ProviderIsFake()
    {
        Assert.Equal("Fake", new OcrOptions().Provider);
    }

    [Fact]
    public void Defaults_StoreRawProviderResponseIsTrue()
    {
        Assert.True(new OcrOptions().StoreRawProviderResponse);
    }

    [Fact]
    public void Defaults_StoreNormalizedOcrResultIsTrue()
    {
        Assert.True(new OcrOptions().StoreNormalizedOcrResult);
    }
}
