using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Profiles;
using DocumentOCR.Application.Services;
using DocumentOCR.Infrastructure.Credits;
using DocumentOCR.Infrastructure.Export;
using DocumentOCR.Infrastructure.Jobs;
using DocumentOCR.Infrastructure.Llm;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Infrastructure.Processing;
using DocumentOCR.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Database ─────────────────────────────────────────────────────────────
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        // ── Storage ──────────────────────────────────────────────────────────────
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddScoped<IDocumentStorageService, LocalDocumentStorageService>();

        // ── OCR provider ─────────────────────────────────────────────────────────
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.Configure<OcrDebugOptions>(configuration.GetSection(OcrDebugOptions.SectionName));
        services.Configure<PdfTextLayerOptions>(configuration.GetSection(PdfTextLayerOptions.SectionName));

        // Selected via "Ocr:Provider" config ("Fake" | "Azure" | "Paddle"); defaults to Fake so
        // local/test environments never accidentally call a real provider without opting in.
        var isAzureProvider = OcrProviderRegistry.IsAzureProvider(configuration);
        var isPaddleProvider = OcrProviderRegistry.IsPaddleProvider(configuration);

        // Fails fast at host startup (not on the first document processed) when Ocr:Provider is
        // "Azure" but Endpoint/ApiKey are missing — a misconfigured deployment should never
        // silently accept uploads it can't actually OCR.
        services.AddOptions<AzureOcrOptions>()
            .Bind(configuration.GetSection(AzureOcrOptions.SectionName))
            .Validate(
                o => !isAzureProvider || o.IsConfigured,
                "AzureDocumentIntelligence:Endpoint and ApiKey must be set (via dotnet user-secrets " +
                "or AzureDocumentIntelligence__Endpoint / __ApiKey environment variables) when " +
                "Ocr:Provider is \"Azure\". See LOCAL_DEVELOPMENT.md.")
            .ValidateOnStart();

        // Same fail-fast treatment when Ocr:Provider is "Paddle" but no BaseUrl was configured.
        services.AddOptions<PaddleOcrOptions>()
            .Bind(configuration.GetSection(PaddleOcrOptions.SectionName))
            .Validate(
                o => !isPaddleProvider || o.IsConfigured,
                "PaddleOcr:BaseUrl must be set (via dotnet user-secrets or PaddleOcr__BaseUrl " +
                "environment variable) when Ocr:Provider is \"Paddle\". See LOCAL_DEVELOPMENT.md.")
            .ValidateOnStart();

        OcrProviderRegistry.Register(services, configuration);

        // ── LLM extraction (PDF text-layer + LLM strategy) ──────────────────────
        // Same fail-fast treatment as Azure/Paddle above, gated on Llm:Enabled instead of a
        // provider-selection string since this is an additive strategy, not a swappable slot.
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .Validate(
                o => !o.Enabled || o.IsConfigured,
                "Llm:Model and Llm:ApiKey must be set (via dotnet user-secrets or Llm__ApiKey " +
                "environment variable) when Llm:Enabled is true.")
            .ValidateOnStart();

        services.AddSingleton<ILlmExtractionClient, GeminiExtractionClient>();
        services.AddScoped<ILlmExtractionCache, LlmExtractionCacheService>();
        services.AddHostedService<LlmStartupWarningService>();

        // ── Document review profiles ────────────────────────────────────────────
        // Pure static data, no per-request state — safe (and cheap) as a singleton.
        services.AddSingleton<IDocumentProfileCatalog, DocumentProfileCatalog>();

        // ── Credits ──────────────────────────────────────────────────────────────
        services.Configure<CreditOptions>(configuration.GetSection(CreditOptions.SectionName));
        services.AddScoped<ICreditService, CreditService>();

        // ── Processing pipeline ──────────────────────────────────────────────────
        services.AddScoped<IFieldExtractionService, FieldExtractionService>();
        services.AddScoped<IFieldNormalizationService, FieldNormalizationService>();
        services.AddScoped<IFieldValidationService, FieldValidationService>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

        // Pure function over the uploaded stream, no per-request state — safe as a singleton.
        services.AddSingleton<IStructuredInvoiceParser, TT78XmlInvoiceParser>();

        // Extraction strategies DocumentProcessingService tries in Priority order (see
        // IDocumentExtractionStrategy remarks): structured XML fast path, then PDF text-layer+LLM,
        // then the OCR pipeline (IDocumentOcrProvider, still resolved as
        // PdfProviderRouter/Fake/Azure/Paddle above). Registered Scoped to match
        // DocumentProcessingService's own lifetime.
        services.AddScoped<IDocumentExtractionStrategy, XmlInvoiceStrategy>();

        // Explicit factory (not a plain AddScoped<IDocumentExtractionStrategy, PdfTextLayerLlmStrategy>())
        // because this strategy needs the concrete PdfTextLayerProvider singleton specifically —
        // resolving IDocumentOcrProvider here would give it PdfProviderRouter (the general DI slot
        // OcrStrategy uses), which would silently fall back to paid OCR internally instead of
        // reporting "not a text-layer PDF" back to DocumentProcessingService's own strategy loop.
        services.AddScoped<IDocumentExtractionStrategy>(sp => new PdfTextLayerLlmStrategy(
            sp.GetRequiredService<PdfTextLayerProvider>(),
            sp.GetRequiredService<ILlmExtractionClient>(),
            sp.GetRequiredService<IFieldNormalizationService>(),
            sp.GetRequiredService<ILlmExtractionCache>(),
            sp.GetRequiredService<IDocumentStorageService>(),
            sp.GetRequiredService<IOptions<LlmOptions>>(),
            sp.GetRequiredService<IOptions<OcrOptions>>(),
            sp.GetRequiredService<ILogger<PdfTextLayerLlmStrategy>>()));

        services.AddScoped<IDocumentExtractionStrategy, OcrStrategy>();

        // Pure functions over already-loaded data, no per-request state — safe as a singleton.
        services.AddSingleton<IReviewTableBuilder, ReviewTableBuilder>();

        // ── Export ───────────────────────────────────────────────────────────────
        services.AddScoped<IExcelExportService, ClosedXmlExportService>();

        // Accounting-service reports (Thông tư 88/2021/TT-BTC) — additive, parallel to the export
        // above. Builder is a pure function over already-loaded data (like IReviewTableBuilder), so
        // it's safe as a singleton; the renderer has no per-request state either.
        services.AddSingleton<IAccountingReportBuilder, AccountingReportBuilder>();
        services.AddSingleton<IAccountingReportWorkbookRenderer, AccountingReportWorkbookRenderer>();
        services.AddScoped<IAccountingReportExportService, AccountingReportExportService>();

        // ── Application services ─────────────────────────────────────────────────
        services.AddScoped<DocumentReviewMappingService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ExportService>();
        services.AddScoped<ClientProfileService>();
        services.AddScoped<IClientAutoSuggestService, ClientAutoSuggestService>();

        // ── Hangfire ─────────────────────────────────────────────────────────────
        services.AddHangfire(config =>
            config.UsePostgreSqlStorage(c =>
                c.UseNpgsqlConnection(
                    configuration.GetConnectionString("DefaultConnection"))));

        services.AddHangfireServer();
        services.AddScoped<DocumentProcessingJob>();

        return services;
    }
}
