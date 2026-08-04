namespace DocumentOCR.Application.Exceptions;

/// <summary>
/// Thrown by <see cref="Interfaces.ICreditService.TryConsumeAsync"/> when consuming the requested
/// amount would push an organization's same-UTC-day credit consumption past
/// <see cref="Credits.CreditOptions.MaxDailyConsumePerOrg"/>. Distinct from a plain
/// <c>false</c> return (insufficient balance) — this is a system-imposed spend cap, not a
/// balance problem, so callers can tell the two rejection reasons apart.
/// </summary>
public class DailyCreditCapExceededException : Exception
{
    public Guid OrganizationId { get; }
    public int DailyCap { get; }
    public long ConsumedToday { get; }
    public int Requested { get; }

    public DailyCreditCapExceededException(Guid organizationId, int dailyCap, long consumedToday, int requested)
        : base("Daily credit spending limit reached for this organization. Please try again tomorrow or contact support.")
    {
        OrganizationId = organizationId;
        DailyCap = dailyCap;
        ConsumedToday = consumedToday;
        Requested = requested;
    }
}
