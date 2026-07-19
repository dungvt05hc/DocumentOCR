using DocumentOCR.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// Resolves and registers the active <see cref="IDocumentOcrProvider"/> from
/// <c>Ocr:Provider</c> config ("Fake" | "Azure" | "Paddle"). Adding another provider is a single
/// new branch here plus its own options class â€” nothing else in the pipeline changes, since
/// everything downstream only depends on <see cref="IDocumentOcrProvider"/>.
/// </summary>
public static class OcrProviderRegistry
{
    public const string FakeProviderName = "Fake";
    public const string AzureProviderName = "Azure";
    public const string PaddleProviderName = "Paddle";

    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var providerName = configuration["Ocr:Provider"];

        // Registered as Singleton: providers are expected to be thread-safe and reusable
        // (the Azure client and the Paddle HttpClient are both designed for reuse across requests).
        if (string.Equals(providerName, AzureProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDocumentOcrProvider, AzureDocumentIntelligenceProvider>();
        }
        else if (string.Equals(providerName, PaddleProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDocumentOcrProvider, PaddleOcrProvider>();
        }
        else
        {
            services.AddSingleton<IDocumentOcrProvider, FakeOcrProvider>();
        }
    }

    /// <summary>True when <c>Ocr:Provider</c> is configured to use Azure Document Intelligence.</summary>
    public static bool IsAzureProvider(IConfiguration configuration) =>
        string.Equals(configuration["Ocr:Provider"], AzureProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <c>Ocr:Provider</c> is configured to use PaddleOCR.</summary>
    public static bool IsPaddleProvider(IConfiguration configuration) =>
        string.Equals(configuration["Ocr:Provider"], PaddleProviderName, StringComparison.OrdinalIgnoreCase);
}
