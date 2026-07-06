using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.Slug).IsUnique();

        // MVP is single-tenant: seed the fixed organization every controller scopes to,
        // so uploads don't fail on the OrganizationId foreign key against a real database.
        builder.HasData(new Organization
        {
            Id = DefaultOrganization.Id,
            Name = DefaultOrganization.Name,
            Slug = DefaultOrganization.Slug,
            CreatedAt = DefaultOrganization.SeededAt,
            UpdatedAt = DefaultOrganization.SeededAt
        });
    }
}
