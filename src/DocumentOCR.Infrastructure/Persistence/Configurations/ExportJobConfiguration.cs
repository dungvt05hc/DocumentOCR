using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.StoredFilePath).HasMaxLength(1000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        // Many-to-many: ExportJob ↔ Document
        builder.HasMany(e => e.Documents)
            .WithMany(d => d.ExportJobs)
            .UsingEntity(j => j.ToTable("ExportJobDocuments"));
    }
}
