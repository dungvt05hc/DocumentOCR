using DocumentOCR.Application.Interfaces;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

public class OcrProviderRegistryTests
{
    private static IConfiguration ConfigWithProvider(string? provider)
    {
        var data = provider is null ? [] : new Dictionary<string, string?> { ["Ocr:Provider"] = provider };
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Theory]
    [InlineData("Azure", true)]
    [InlineData("azure", true)]
    [InlineData("Fake", false)]
    [InlineData("Paddle", false)]
    [InlineData(null, false)]
    public void IsAzureProvider_ReturnsExpected(string? provider, bool expected)
    {
        Assert.Equal(expected, OcrProviderRegistry.IsAzureProvider(ConfigWithProvider(provider)));
    }

    [Theory]
    [InlineData("Paddle", true)]
    [InlineData("paddle", true)]
    [InlineData("Fake", false)]
    [InlineData("Azure", false)]
    [InlineData(null, false)]
    public void IsPaddleProvider_ReturnsExpected(string? provider, bool expected)
    {
        Assert.Equal(expected, OcrProviderRegistry.IsPaddleProvider(ConfigWithProvider(provider)));
    }

    [Theory]
    [InlineData("Fake", typeof(FakeOcrProvider))]
    [InlineData(null, typeof(FakeOcrProvider))]
    [InlineData("Azure", typeof(AzureDocumentIntelligenceProvider))]
    [InlineData("Paddle", typeof(PaddleOcrProvider))]
    [InlineData("SomethingUnknown", typeof(FakeOcrProvider))]
    public void Register_SelectsExpectedProviderImplementation(string? provider, Type expectedType)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AzureOcrOptions>(_ => { });
        services.Configure<PaddleOcrOptions>(_ => { });

        OcrProviderRegistry.Register(services, ConfigWithProvider(provider));

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IDocumentOcrProvider>();

        Assert.IsType(expectedType, resolved);
    }
}
