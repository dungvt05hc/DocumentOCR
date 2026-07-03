using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Infrastructure.Processing;

public class UsageTrackingService : IUsageTrackingService
{
    private readonly IApplicationDbContext _db;

    public UsageTrackingService(IApplicationDbContext db) => _db = db;

    public async Task TrackAsync(
        string providerName,
        int pageCount,
        long processingDurationMs,
        decimal estimatedCost,
        CancellationToken ct = default)
    {
        _db.UsageLogs.Add(new UsageLog
        {
            ProviderName = providerName,
            PageCount = pageCount,
            ProcessingDurationMs = processingDurationMs,
            EstimatedCost = estimatedCost
        });

        await _db.SaveChangesAsync(ct);
    }
}
