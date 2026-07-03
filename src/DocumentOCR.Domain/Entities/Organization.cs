using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

public class Organization : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
