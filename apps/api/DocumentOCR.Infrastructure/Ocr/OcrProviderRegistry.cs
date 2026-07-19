using DocumentOCR.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// Resolves and registers the active <see cref="IDocumentOcrProvider"/> from
/// <c>Ocr:Provider</c> config ("Fake" | "Azure"). Adding a new provider (e.g. PaddleOCR) is a
/// single new branch here plus its own options class â€” nothing else in the pipeline changes,
/// since everything downstream only depends on <see cref="IDocumentOcrProvider"/>.
/// </summary>
public static class OcrProviderRegistry
{
    public const string FakeProviderName = "Fake";
    public const string AzureProviderName = "Azure";

    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var providerName = configuration["Ocr:Provider"];

        // Registered as Singleton: providers are expected to be thread-safe and reusable
        // (the Azure client in particular is designed for reuse across requests).
        if (string.Equals(providerName, AzureProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDocumentOcrProvider, AzureDocumentIntelligenceProvider>();
        }
        else
        {
            services.AddSingleton<IDocumentOcrProvider, FakeOcrProvider>();
        }
    }

    /// <summary>True when <c>Ocr:Provider</c> is configured to use Azure Document Intelligence.</summary>
    public static bool IsAzureProvider(IConfiguration configuration) =>
        string.Equals(configuration["Ocr:Provider"], AzureProviderName, StringComparison.OrdinalIgnoreCase);
}
