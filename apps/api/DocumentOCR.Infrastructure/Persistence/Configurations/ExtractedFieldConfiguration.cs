using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class ExtractedFieldConfiguration : IEntityTypeConfiguration<ExtractedField>
{
    public void Configure(EntityTypeBuilder<ExtractedField> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.FieldName).HasMaxLength(100).IsRequired();
        builder.Property(f => f.RawValue).HasMaxLength(2000);
        builder.Property(f => f.NormalizedValue).HasMaxLength(2000);
        builder.Property(f => f.BoundingBoxJson).HasMaxLength(1000);
        builder.Property(f => f.ExtractionMethod).HasMaxLength(50);
        builder.Property(f => f.SourceText).HasMaxLength(300);

        builder.HasIndex(f => new { f.DocumentId, f.FieldName }).IsUnique();
    }
}
