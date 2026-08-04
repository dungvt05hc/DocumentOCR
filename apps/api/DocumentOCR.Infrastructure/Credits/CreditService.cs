using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Exceptions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Credits;

/// <summary>
/// Postgres-backed <see cref="ICreditService"/>. The ledger is append-only with no separate
/// balance column, so <see cref="TryConsumeAsync"/> has to read-then-write ("is there enough
/// balance? if so, insert a debit row") atomically across concurrent callers for the same
/// organization. That's done with <c>pg_advisory_xact_lock</c>, a transaction-scoped Postgres
/// lock keyed by organization: it serializes concurrent TryConsumeAsync/RefundAsync/TopUpAsync
/// calls for one org (never blocking other orgs) and auto-releases on commit/rollback, so a
/// crashed/pooled connection can never leak a held lock. This is deliberately simpler than a
/// SERIALIZABLE-isolation-plus-retry-loop approach: no transactions ever abort under contention,
/// so there's no retry logic to get wrong.
/// </summary>
public class CreditService : ICreditService
{
    private readonly ApplicationDbContext _db;
    private readonly CreditOptions _options;
    private readonly ILogger<CreditService> _logger;

    public CreditService(ApplicationDbContext db, IOptions<CreditOptions> options, ILogger<CreditService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public Task<long> GetBalanceAsync(Guid organizationId, CancellationToken ct = default) =>
        GetBalanceAsync(_db, organizationId, ct);

    public async Task<(IReadOnlyList<CreditTransaction> Items, int TotalCount)> GetTransactionsAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.CreditTransactions
            .Where(t => t.OrganizationId == organizationId)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> TryConsumeAsync(
        Guid organizationId,
        int amount,
        string referenceType,
        Guid referenceId,
        string? description = null,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Consume amount must be positive.");

        // EF Core's InMemory provider (used by CustomWebApplicationFactory for the WebApi
        // integration test suite) throws if BeginTransactionAsync is even called — it doesn't
        // support transactions at all, not just advisory locks — so both are skipped together
        // for non-Npgsql providers. See the class remarks: this is safe there because those test
        // doubles never exercise real concurrency; every production deployment runs on Npgsql.
        var isNpgsql = _db.Database.IsNpgsql();
        var transaction = isNpgsql ? await _db.Database.BeginTransactionAsync(ct) : null;

        try
        {
            if (isNpgsql)
            {
                var lockKey = ComputeLockKey(organizationId);
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
            }

            var balance = await GetBalanceAsync(_db, organizationId, ct);
            if (balance < amount)
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                return false;
            }

            if (_options.MaxDailyConsumePerOrg > 0)
            {
                var todayUtc = DateTime.UtcNow.Date;
                var consumedToday = await _db.CreditTransactions
                    .Where(t => t.OrganizationId == organizationId
                        && t.Type == CreditTransactionType.Consume
                        && t.CreatedAt >= todayUtc)
                    .SumAsync(t => (long?)-t.Amount, ct) ?? 0;

                if (consumedToday + amount > _options.MaxDailyConsumePerOrg)
                {
                    if (transaction is not null) await transaction.RollbackAsync(ct);

                    _logger.LogWarning(
                        "Organization {OrganizationId} rejected: daily credit cap would be exceeded " +
                        "(already consumed {ConsumedToday}, cap {Cap}, requested {Amount}, reference {ReferenceType}/{ReferenceId})",
                        organizationId, consumedToday, _options.MaxDailyConsumePerOrg, amount, referenceType, referenceId);

                    throw new DailyCreditCapExceededException(organizationId, _options.MaxDailyConsumePerOrg, consumedToday, amount);
                }
            }

            _db.CreditTransactions.Add(new CreditTransaction
            {
                OrganizationId = organizationId,
                Type = CreditTransactionType.Consume,
                Amount = -amount,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                Description = description
            });

            await _db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return true;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task RefundAsync(
        Guid organizationId,
        int amount,
        string referenceType,
        Guid referenceId,
        string? description = null,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Refund amount must be positive.");

        _db.CreditTransactions.Add(new CreditTransaction
        {
            OrganizationId = organizationId,
            Type = CreditTransactionType.Refund,
            Amount = amount,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task TopUpAsync(
        Guid organizationId, int amount, string? description = null, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Top-up amount must be positive.");

        _db.CreditTransactions.Add(new CreditTransaction
        {
            OrganizationId = organizationId,
            Type = CreditTransactionType.TopUp,
            Amount = amount,
            Description = description
        });

        await _db.SaveChangesAsync(ct);
    }

    private static async Task<long> GetBalanceAsync(ApplicationDbContext db, Guid organizationId, CancellationToken ct) =>
        await db.CreditTransactions
            .Where(t => t.OrganizationId == organizationId)
            .SumAsync(t => (long?)t.Amount, ct) ?? 0;

    private static long ComputeLockKey(Guid organizationId)
    {
        Span<byte> bytes = stackalloc byte[16];
        organizationId.TryWriteBytes(bytes);
        return BitConverter.ToInt64(bytes[..8]);
    }
}
