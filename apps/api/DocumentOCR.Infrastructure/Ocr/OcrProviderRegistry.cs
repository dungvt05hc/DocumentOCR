using DocumentOCR.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// Resolves and registers the active <see cref="IDocumentOcrProvider"/> from
/// <c>Ocr:Provider</c> config ("Fake" | "Azure" | "Paddle"). Adding another provider is a single
/// new branch here plus its own options class -- nothing else in the pipeline changes, since
/// everything downstream only depends on <see cref="IDocumentOcrProvider"/>.
/// <para>
/// The configured provider is registered under its own concrete type first, then wrapped by
/// <see cref="PdfProviderRouter"/> (unless <c>Ocr:PdfTextLayer:Enabled</c> is false) so PDF
/// uploads with a usable text layer skip OCR entirely -- see <see cref="PdfProviderRouter"/> and
/// <see cref="PdfTextLayerProvider"/>. The final <see cref="IDocumentOcrProvider"/> registration
/// is what <c>DocumentProcessingService</c> resolves; it never needs to know whether a request
/// went through the router or straight to the configured provider.
/// </para>
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
        // Registered under their own concrete type (not IDocumentOcrProvider directly) so the
        // factory below can resolve the configured provider regardless of whether it ends up
        // wrapped by PdfProviderRouter.
        if (string.Equals(providerName, AzureProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<AzureDocumentIntelligenceProvider>();
        }
        else if (string.Equals(providerName, PaddleProviderName, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<PaddleOcrProvider>();
        }
        else
        {
            services.AddSingleton<FakeOcrProvider>();
        }

        services.AddSingleton<PdfTextLayerProvider>();

        services.AddSingleton<IDocumentOcrProvider>(sp =>
        {
            IDocumentOcrProvider configuredProvider = ResolveConfiguredProvider(sp, providerName);

            var pdfTextLayerEnabled = sp.GetRequiredService<IOptions<PdfTextLayerOptions>>().Value.Enabled;
            if (!pdfTextLayerEnabled)
                return configuredProvider;

            return new PdfProviderRouter(
                sp.GetRequiredService<PdfTextLayerProvider>(),
                configuredProvider,
                sp.GetRequiredService<ILogger<PdfProviderRouter>>());
        });
    }

    private static IDocumentOcrProvider ResolveConfiguredProvider(IServiceProvider sp, string? providerName)
    {
        if (string.Equals(providerName, AzureProviderName, StringComparison.OrdinalIgnoreCase))
            return sp.GetRequiredService<AzureDocumentIntelligenceProvider>();

        if (string.Equals(providerName, PaddleProviderName, StringComparison.OrdinalIgnoreCase))
            return sp.GetRequiredService<PaddleOcrProvider>();

        return sp.GetRequiredService<FakeOcrProvider>();
    }

    /// <summary>True when <c>Ocr:Provider</c> is configured to use Azure Document Intelligence.</summary>
    public static bool IsAzureProvider(IConfiguration configuration) =>
        string.Equals(configuration["Ocr:Provider"], AzureProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <c>Ocr:Provider</c> is configured to use PaddleOCR.</summary>
    public static bool IsPaddleProvider(IConfiguration configuration) =>
        string.Equals(configuration["Ocr:Provider"], PaddleProviderName, StringComparison.OrdinalIgnoreCase);
}
