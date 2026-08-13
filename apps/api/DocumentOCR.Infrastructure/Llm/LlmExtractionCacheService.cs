using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Infrastructure.Llm;

/// <summary>Postgres-backed <see cref="ILlmExtractionCache"/> — a plain lookup table, no ledger/locking semantics needed (unlike <c>CreditService</c>).</summary>
public sealed class LlmExtractionCacheService : ILlmExtractionCache
{
    private readonly IApplicationDbContext _db;

    public LlmExtractionCacheService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string?> TryGetResponseJsonAsync(string textHash, string model, CancellationToken ct = default)
    {
        var entry = await _db.LlmExtractionCaches
            .Where(c => c.TextHash == textHash && c.Model == model)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return entry?.ResponseJson;
    }

    public async Task SetAsync(string textHash, string model, string responseJson, CancellationToken ct = default)
    {
        _db.LlmExtractionCaches.Add(new LlmExtractionCache
        {
            TextHash = textHash,
            Model = model,
            ResponseJson = responseJson
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique (TextHash, Model) violation from a concurrent identical extraction racing
            // this one -- another call already cached the same response. Losing this particular
            // write is harmless (no caching benefit for this call, nothing else depends on it), so
            // it's swallowed rather than failing the document.
            _db.ChangeTracker.Clear();
        }
    }
}
