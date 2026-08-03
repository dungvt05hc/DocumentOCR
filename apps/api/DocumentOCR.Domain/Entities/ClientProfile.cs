using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Domain.Entities;

/// <summary>
/// A client of the accounting-service user (e.g. a household business or small enterprise whose
/// bookkeeping they manage) — distinct from <see cref="Organization"/>, which is the single tenant
/// in this MVP. Lets documents be grouped/filtered per end-client.
/// </summary>
public class ClientProfile : BaseEntity
{
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public ClientType ClientType { get; set; } = ClientType.HouseholdBusiness;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public Organization Organization { get; set; } = null!;
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
