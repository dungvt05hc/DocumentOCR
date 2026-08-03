using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class ClientProfileConfiguration : IEntityTypeConfiguration<ClientProfile>
{
    public void Configure(EntityTypeBuilder<ClientProfile> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.TaxCode).HasMaxLength(20);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.ClientType).HasConversion<string>().HasMaxLength(50);

        // Partial unique index: multiple clients with no tax code on file must not collide.
        builder.HasIndex(c => new { c.OrganizationId, c.TaxCode })
            .IsUnique()
            .HasFilter("\"TaxCode\" IS NOT NULL");

        builder.HasOne(c => c.Organization)
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
