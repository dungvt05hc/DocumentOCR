using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Common;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DocumentOCR.IntegrationTests;

/// <summary>
/// Boots the real WebApi host (real controllers, real DI graph, real Application/Infrastructure
/// services) but swaps the three external dependencies the MVP explicitly keeps local-only:
/// Postgres → EF Core InMemory, Azure OCR → FakeOcrProvider, Hangfire → a recording fake so
/// processing can be driven synchronously instead of racing a real background worker.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly string _storageBasePath = Path.Combine(Path.GetTempPath(), "DocumentOCR-IntegrationTests", Guid.NewGuid().ToString());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _storageBasePath
            });
        });

        builder.ConfigureServices(services =>
        {
            // The app's own AddDbContext call already registered Npgsql's provider services
            // into this same container; adding UseInMemoryDatabase alongside it would make EF
            // see two registered providers. Giving the InMemory provider its own isolated
            // internal service provider (the standard WebApplicationFactory + EF pattern)
            // avoids that clash entirely.
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .UseInternalServiceProvider(inMemoryServiceProvider));

            services.RemoveAll<IDocumentOcrProvider>();
            services.AddSingleton<IDocumentOcrProvider, FakeOcrProvider>();

            services.RemoveAll<IBackgroundJobClient>();
            services.AddSingleton<IBackgroundJobClient, RecordingBackgroundJobClient>();

            // The real Hangfire server hosted service polls Postgres for jobs; without a
            // reachable database it would hang trying to connect and time out host shutdown,
            // so it's removed entirely. The RecordingBackgroundJobClient above means nothing is
            // ever enqueued into it. AddHangfireServer() registers its IHostedService via a
            // provider factory (no ImplementationType), so it can't be matched by type — instead
            // match any IHostedService whose implementation originates from a Hangfire assembly.
            var hangfireHostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .Where(IsFromHangfireAssembly)
                .ToList();
            foreach (var descriptor in hangfireHostedServices)
                services.Remove(descriptor);
        });
    }

    private static bool IsFromHangfireAssembly(ServiceDescriptor descriptor)
    {
        var declaringAssembly = descriptor.ImplementationType?.Assembly
            ?? descriptor.ImplementationInstance?.GetType().Assembly
            ?? descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly;

        return declaringAssembly?.GetName().Name?.StartsWith("Hangfire", StringComparison.Ordinal) == true;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();

        // Every test that uploads/reprocesses a document now goes through the credit-charging
        // integration point in DocumentsController, so the default org needs a starting balance
        // or every one of those requests would be rejected as insufficient-balance before this
        // test suite even gets to exercise the pipeline it's actually testing.
        scope.ServiceProvider.GetRequiredService<ICreditService>()
            .TopUpAsync(DefaultOrganization.Id, 100_000, "Integration test seed balance")
            .GetAwaiter().GetResult();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (Directory.Exists(_storageBasePath))
            Directory.Delete(_storageBasePath, recursive: true);
    }
}
