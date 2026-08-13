using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentOCR.Infrastructure.Persistence.Configurations;

public class LlmExtractionCacheConfiguration : IEntityTypeConfiguration<LlmExtractionCache>
{
    public void Configure(EntityTypeBuilder<LlmExtractionCache> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TextHash).HasMaxLength(64).IsRequired(); // SHA-256 hex digest
        builder.Property(c => c.Model).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ResponseJson).IsRequired();

        // Same text under a different model isn't the same cache entry -- a model change
        // shouldn't silently replay a previous model's output.
        builder.HasIndex(c => new { c.TextHash, c.Model }).IsUnique();
    }
}
