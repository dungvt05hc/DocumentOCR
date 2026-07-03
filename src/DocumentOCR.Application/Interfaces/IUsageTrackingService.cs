namespace DocumentOCR.Application.Interfaces;

/// <summary>Records OCR provider usage for billing and audit purposes.</summary>
public interface IUsageTrackingService
{
    Task TrackAsync(
        string providerName,
        int pageCount,
        long processingDurationMs,
        decimal estimatedCost,
        CancellationToken ct = default);
}
