using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class UsageLogConfiguration : IEntityTypeConfiguration<UsageLog>
{
    public void Configure(EntityTypeBuilder<UsageLog> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.ProviderName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.EstimatedCost).HasColumnType("numeric(18,6)");

        builder.HasIndex(u => u.CreatedAt);
    }
}
