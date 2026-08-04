using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Domain.Entities;

/// <summary>
/// A single append-only entry in an organization's prepaid credit ledger. There is no separate
/// balance table — an organization's current balance is always <c>SUM(Amount)</c> over its rows.
/// <see cref="Type"/> is TopUp/Refund for positive-value or Consume for negative-value entries.
/// </summary>
public class CreditTransaction : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public CreditTransactionType Type { get; set; }

    /// <summary>Positive for TopUp/Refund, negative for Consume.</summary>
    public int Amount { get; set; }

    /// <summary>What this entry relates to (e.g. "Document"). Null for a manual TopUp.</summary>
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
}
