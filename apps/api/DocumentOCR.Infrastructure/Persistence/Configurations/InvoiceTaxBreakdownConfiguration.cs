using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class InvoiceTaxBreakdownConfiguration : IEntityTypeConfiguration<InvoiceTaxBreakdown>
{
    public void Configure(EntityTypeBuilder<InvoiceTaxBreakdown> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.RawVatRate).HasMaxLength(50);
        builder.Property(t => t.VatRate).HasMaxLength(20);
        builder.Property(t => t.TaxableAmount).HasColumnType("numeric(18,2)");
        builder.Property(t => t.TaxAmount).HasColumnType("numeric(18,2)");

        builder.HasIndex(t => new { t.DocumentId, t.SortOrder });

        builder.HasOne(t => t.Document)
            .WithMany(d => d.TaxBreakdowns)
            .HasForeignKey(t => t.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
