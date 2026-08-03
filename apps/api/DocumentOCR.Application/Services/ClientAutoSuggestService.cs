using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

/// <summary>See <see cref="IClientAutoSuggestService"/>.</summary>
public class ClientAutoSuggestService : IClientAutoSuggestService
{
    private readonly IApplicationDbContext _db;
    private readonly IFieldNormalizationService _normalization;

    public ClientAutoSuggestService(IApplicationDbContext db, IFieldNormalizationService normalization)
    {
        _db = db;
        _normalization = normalization;
    }

    public async Task<bool> TrySuggestAndAssignAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null || document.ClientProfileId is not null)
            return false;

        var supplierTaxCodeField = document.Fields
            .FirstOrDefault(f => f.FieldName == nameof(FieldName.SupplierTaxCode));

        var supplierTaxCode = _normalization.NormalizeTaxCode(
            supplierTaxCodeField?.NormalizedValue ?? supplierTaxCodeField?.RawValue);

        if (supplierTaxCode is null)
            return false;

        var match = await _db.ClientProfiles.FirstOrDefaultAsync(
            c => c.OrganizationId == document.OrganizationId
                 && c.IsActive
                 && c.TaxCode == supplierTaxCode,
            ct);

        if (match is null)
            return false;

        document.ClientProfileId = match.Id;
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return true;
    }
}
