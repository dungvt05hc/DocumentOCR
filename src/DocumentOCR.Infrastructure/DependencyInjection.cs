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
        services.Configure<AzureOcrOptions>(
            configuration.GetSection(AzureOcrOptions.SectionName));
        // Registered as Singleton: DocumentIntelligenceClient is thread-safe and designed for reuse.
        services.AddSingleton<IDocumentOcrProvider, AzureDocumentIntelligenceProvider>();

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
