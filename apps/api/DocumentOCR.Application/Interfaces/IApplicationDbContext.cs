using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DocumentOCR.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<ClientProfile> ClientProfiles { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentPage> DocumentPages { get; }
    DbSet<ExtractedField> ExtractedFields { get; }
    DbSet<ValidationWarning> ValidationWarnings { get; }
    DbSet<OcrProviderLog> OcrProviderLogs { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }

    /// <summary>
    /// Exposed so a caller can recover from a failed <see cref="SaveChangesAsync"/> by discarding
    /// whatever invalid state is still tracked (<see cref="ChangeTracker.Clear"/>) before a
    /// smaller, targeted retry — see <c>DocumentProcessingService.MarkFailedAsync</c>.
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
