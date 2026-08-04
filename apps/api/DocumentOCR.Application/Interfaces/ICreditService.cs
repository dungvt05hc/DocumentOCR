using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Prepaid-credit ledger. There is no separate balance table — balance is always derived from
/// the append-only <see cref="CreditTransaction"/> rows. Implementations must make
/// <see cref="TryConsumeAsync"/> safe under concurrent calls for the same organization (never
/// letting the balance go negative), which requires a real infrastructure dependency
/// (transaction + locking), hence this interface's only implementation lives in Infrastructure.
/// </summary>
public interface ICreditService
{
    Task<long> GetBalanceAsync(Guid organizationId, CancellationToken ct = default);

    Task<(IReadOnlyList<CreditTransaction> Items, int TotalCount)> GetTransactionsAsync(
        Guid organizationId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Atomically checks balance and daily cap, then debits <paramref name="amount"/> if
    /// sufficient. Returns false when the balance is insufficient. Throws
    /// <see cref="Exceptions.DailyCreditCapExceededException"/> when the organization's daily
    /// consumption cap would be exceeded (a distinct rejection reason from insufficient balance).
    /// </summary>
    Task<bool> TryConsumeAsync(
        Guid organizationId,
        int amount,
        string referenceType,
        Guid referenceId,
        string? description = null,
        CancellationToken ct = default);

    Task RefundAsync(
        Guid organizationId,
        int amount,
        string referenceType,
        Guid referenceId,
        string? description = null,
        CancellationToken ct = default);

    Task TopUpAsync(
        Guid organizationId,
        int amount,
        string? description = null,
        CancellationToken ct = default);
}
