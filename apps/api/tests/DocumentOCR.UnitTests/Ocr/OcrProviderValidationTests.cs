using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

/// <summary>
/// Exercises the same AddOptions&lt;AzureOcrOptions&gt;().Validate().ValidateOnStart() wiring
/// registered in DependencyInjection.AddInfrastructure, in isolation from the full DI graph —
/// verifies that resolving IOptions&lt;AzureOcrOptions&gt; actually throws when Ocr:Provider is
/// "Azure" but credentials are missing, rather than only testing the predicate in isolation.
/// </summary>
public class OcrProviderValidationTests
{
    private static ServiceProvider BuildProvider(string? ocrProvider, string endpoint = "", string apiKey = "")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ocr:Provider"] = ocrProvider,
                ["AzureDocumentIntelligence:Endpoint"] = endpoint,
                ["AzureDocumentIntelligence:ApiKey"] = apiKey
            })
            .Build();

        var services = new ServiceCollection();
        var isAzureProvider = string.Equals(ocrProvider, "Azure", StringComparison.OrdinalIgnoreCase);

        services.AddOptions<AzureOcrOptions>()
            .Bind(configuration.GetSection(AzureOcrOptions.SectionName))
            .Validate(
                o => !isAzureProvider || o.IsConfigured,
                "AzureDocumentIntelligence:Endpoint and ApiKey must be set when Ocr:Provider is \"Azure\".")
            .ValidateOnStart();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvingAzureOcrOptions_AzureProviderWithoutCredentials_ThrowsOptionsValidationException()
    {
        using var provider = BuildProvider(ocrProvider: "Azure");

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AzureOcrOptions>>().Value);

        Assert.Contains("Endpoint and ApiKey must be set", ex.Message);
    }

    [Fact]
    public void ResolvingAzureOcrOptions_AzureProviderWithCredentials_DoesNotThrow()
    {
        using var provider = BuildProvider(
            ocrProvider: "Azure", endpoint: "https://example.cognitiveservices.azure.com/", apiKey: "some-key");

        var options = provider.GetRequiredService<IOptions<AzureOcrOptions>>().Value;

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void ResolvingAzureOcrOptions_FakeProviderWithoutCredentials_DoesNotThrow()
    {
        using var provider = BuildProvider(ocrProvider: "Fake");

        var options = provider.GetRequiredService<IOptions<AzureOcrOptions>>().Value;

        Assert.False(options.IsConfigured);
    }
}
