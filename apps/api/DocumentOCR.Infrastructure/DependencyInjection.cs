using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Services;
using DocumentOCR.Infrastructure.Export;
using DocumentOCR.Infrastructure.Jobs;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Infrastructure.Processing;
using DocumentOCR.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        // Selected via "Ocr:Provider" config ("Fake" | "Azure"); defaults to Fake so
        // local/test environments never accidentally call Azure without opting in.
        var ocrProviderName = configuration["Ocr:Provider"];
        var isAzureProvider = string.Equals(ocrProviderName, "Azure", StringComparison.OrdinalIgnoreCase);

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

        if (isAzureProvider)
        {
            // Registered as Singleton: DocumentIntelligenceClient is thread-safe and designed for reuse.
            services.AddSingleton<IDocumentOcrProvider, AzureDocumentIntelligenceProvider>();
        }
        else
        {
            services.AddSingleton<IDocumentOcrProvider, FakeOcrProvider>();
        }

        // ── Processing pipeline ──────────────────────────────────────────────────
        services.AddScoped<IFieldExtractionService, FieldExtractionService>();
        services.AddScoped<IFieldNormalizationService, FieldNormalizationService>();
        services.AddScoped<IFieldValidationService, FieldValidationService>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

        // ── Export ───────────────────────────────────────────────────────────────
        services.AddScoped<IExcelExportService, ClosedXmlExportService>();

        // ── Application services ─────────────────────────────────────────────────
        services.AddScoped<DocumentService>();
        services.AddScoped<ExportService>();

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
