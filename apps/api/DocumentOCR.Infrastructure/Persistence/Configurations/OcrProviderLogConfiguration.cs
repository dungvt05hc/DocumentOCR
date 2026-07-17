using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class OcrProviderLogConfiguration : IEntityTypeConfiguration<OcrProviderLog>
{
    public void Configure(EntityTypeBuilder<OcrProviderLog> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.ProviderName).HasMaxLength(100).IsRequired();
        builder.Property(l => l.ModelId).HasMaxLength(100);
        builder.Property(l => l.ErrorMessage).HasMaxLength(2000);
        builder.Property(l => l.EstimatedCost).HasColumnType("numeric(18,6)");
        builder.Property(l => l.RawResponseJson).HasColumnType("jsonb");

        builder.HasOne(l => l.Document)
            .WithMany(d => d.OcrProviderLogs)
            .HasForeignKey(l => l.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.DocumentId);
        builder.HasIndex(l => l.CreatedAt);
    }
}
