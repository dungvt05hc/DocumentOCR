using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class AppUser : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public Organization Organization { get; set; } = null!;
}
