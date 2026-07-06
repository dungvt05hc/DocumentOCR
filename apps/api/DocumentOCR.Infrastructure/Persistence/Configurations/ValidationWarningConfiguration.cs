using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class ValidationWarningConfiguration : IEntityTypeConfiguration<ValidationWarning>
{
    public void Configure(EntityTypeBuilder<ValidationWarning> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.FieldName).HasMaxLength(100);
        builder.Property(w => w.WarningCode).HasMaxLength(100);
        builder.Property(w => w.Message).HasMaxLength(1000).IsRequired();
        builder.Property(w => w.Severity).HasConversion<string>().HasMaxLength(20);
    }
}
