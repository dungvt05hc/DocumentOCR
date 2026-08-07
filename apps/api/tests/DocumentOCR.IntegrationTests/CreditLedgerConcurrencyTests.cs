using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Exceptions;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Infrastructure.Credits;
using DocumentOCR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace DocumentOCR.IntegrationTests;

/// <summary>
/// Exercises <see cref="CreditService"/> against a real Postgres container rather than EF Core's
/// InMemory provider (used by <see cref="CustomWebApplicationFactory"/> for the rest of this test
/// suite): <c>pg_advisory_xact_lock</c> — the mechanism <see cref="CreditService.TryConsumeAsync"/>
/// relies on to stay safe under concurrent calls for the same organization — is raw Postgres SQL
/// that InMemory can't execute at all. Each test gets its own container/database, and each
/// concurrent call below opens its own <see cref="ApplicationDbContext"/>/connection against it,
/// mirroring how independent HTTP requests would each get their own scoped DbContext in
/// production. Requires Docker to be running.
/// </summary>
public class CreditLedgerConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _organizationId = Guid.NewGuid();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        db.Organizations.Add(new Organization
        {
            Id = _organizationId,
            Name = "Test Organization",
            Slug = $"test-organization-{_organizationId:N}"
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task TryConsumeAsync_TwentyParallelRequests_OnlyAsManyAsBalanceAllowsSucceed()
    {
        await TopUpAsync(10);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ConsumeOneAsync()));

        Assert.Equal(10, results.Count(succeeded => succeeded));
        Assert.Equal(10, results.Count(succeeded => !succeeded));

        var finalBalance = await GetBalanceAsync();
        Assert.Equal(0, finalBalance);
    }

    [Fact]
    public async Task TryConsumeAsync_InsufficientBalance_ReturnsFalseAndDoesNotChangeBalance()
    {
        await TopUpAsync(5);

        await using var db = CreateDbContext();
        var service = CreateService(db);
        var consumed = await service.TryConsumeAsync(_organizationId, 10, "Document", Guid.NewGuid());

        Assert.False(consumed);
        Assert.Equal(5, await GetBalanceAsync());
    }

    [Fact]
    public async Task TryConsumeAsync_ExceedsDailyCap_ThrowsAndDoesNotChangeBalance()
    {
        await TopUpAsync(100);

        await using var db = CreateDbContext();
        var service = CreateService(db, new CreditOptions { Enabled = true, MaxDailyConsumePerOrg = 5 });

        await Assert.ThrowsAsync<DailyCreditCapExceededException>(
            () => service.TryConsumeAsync(_organizationId, 10, "Document", Guid.NewGuid()));

        Assert.Equal(100, await GetBalanceAsync());
    }

    [Fact]
    public async Task RefundAsync_AddsBackTheConsumedAmount()
    {
        await TopUpAsync(10);

        await using var db = CreateDbContext();
        var service = CreateService(db);
        var documentId = Guid.NewGuid();

        Assert.True(await service.TryConsumeAsync(_organizationId, 4, "Document", documentId));
        Assert.Equal(6, await GetBalanceAsync());

        await service.RefundAsync(_organizationId, 4, "Document", documentId, "Processing failed permanently");
        Assert.Equal(10, await GetBalanceAsync());
    }

    private async Task<bool> ConsumeOneAsync()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        return await service.TryConsumeAsync(_organizationId, 1, "Document", Guid.NewGuid());
    }

    private async Task TopUpAsync(int amount)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        await service.TopUpAsync(_organizationId, amount, "Test seed balance");
    }

    private async Task<long> GetBalanceAsync()
    {
        await using var db = CreateDbContext();
        return await CreateService(db).GetBalanceAsync(_organizationId);
    }

    // Enabled=true explicitly: these tests exercise the charging engine itself, so they must opt
    // into it regardless of CreditOptions.Enabled's development-friendly default of false.
    private static CreditService CreateService(ApplicationDbContext db, CreditOptions? options = null) =>
        new(db, Options.Create(options ?? new CreditOptions { Enabled = true }), NullLogger<CreditService>.Instance);

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
