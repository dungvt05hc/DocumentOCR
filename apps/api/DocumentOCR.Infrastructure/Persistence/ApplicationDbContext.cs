using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<ClientProfile> ClientProfiles => Set<ClientProfile>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();
    public DbSet<ExtractedField> ExtractedFields => Set<ExtractedField>();
    public DbSet<ValidationWarning> ValidationWarnings => Set<ValidationWarning>();
    public DbSet<OcrProviderLog> OcrProviderLogs => Set<OcrProviderLog>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();
    public DbSet<InvoiceTaxBreakdown> InvoiceTaxBreakdowns => Set<InvoiceTaxBreakdown>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<DocumentOCR.Domain.Common.BaseEntity>()
                     .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
