using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
