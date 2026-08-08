using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.OriginalFileName).HasMaxLength(500).IsRequired();
        builder.Property(d => d.StoredFilePath).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ErrorMessage).HasMaxLength(2000);
        builder.Property(d => d.TablesJson).HasColumnType("jsonb");

        builder.HasIndex(d => new { d.OrganizationId, d.ClientProfileId, d.CreatedAt });

        builder.HasOne(d => d.Organization)
            .WithMany(o => o.Documents)
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.ClientProfile)
            .WithMany(c => c.Documents)
            .HasForeignKey(d => d.ClientProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(d => d.Pages)
            .WithOne(p => p.Document)
            .HasForeignKey(p => p.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Fields)
            .WithOne(f => f.Document)
            .HasForeignKey(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.ValidationWarnings)
            .WithOne(w => w.Document)
            .HasForeignKey(w => w.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
