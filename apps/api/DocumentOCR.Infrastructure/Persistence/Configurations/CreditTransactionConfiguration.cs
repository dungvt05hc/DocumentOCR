using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ReferenceType).HasMaxLength(100);
        builder.Property(t => t.Description).HasMaxLength(500);

        // Ledger is append-only and always queried "for this org, newest first" (balance sum,
        // transaction history, daily-cap window) — never joined through Organization's own
        // navigation, so a FK-only relation (no collection on Organization) is enough.
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.OrganizationId, t.CreatedAt });
    }
}
