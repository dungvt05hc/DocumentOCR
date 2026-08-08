using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

/// <summary>See <see cref="IClientAutoSuggestService"/>.</summary>
public class ClientAutoSuggestService : IClientAutoSuggestService
{
    private const string DirectionUnknownWarningCode = "DOCUMENT_DIRECTION_UNKNOWN";

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

        var supplierTaxCode = NormalizedFieldValue(document, nameof(FieldName.SupplierTaxCode));
        var buyerTaxCode = NormalizedFieldValue(document, nameof(FieldName.BuyerTaxCode));

        if (supplierTaxCode is null && buyerTaxCode is null)
            return false;

        // Seller-side match (client is the seller — hóa đơn đầu ra) is tried first; buyer-side
        // (client is the buyer — hóa đơn đầu vào) only when the seller side didn't match, since a
        // document can't simultaneously be both for the same client.
        var match = supplierTaxCode is not null
            ? await FindActiveClientAsync(document.OrganizationId, supplierTaxCode, ct)
            : null;
        match ??= buyerTaxCode is not null
            ? await FindActiveClientAsync(document.OrganizationId, buyerTaxCode, ct)
            : null;

        if (match is null)
            return false;

        document.ClientProfileId = match.Id;
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<DocumentDirection> InferDirectionAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents
            .Include(d => d.Fields)
            .Include(d => d.ClientProfile)
            .Include(d => d.ValidationWarnings)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
            return DocumentDirection.Unknown;

        var clientTaxCode = document.ClientProfile?.TaxCode;
        var direction = DocumentDirection.Unknown;

        if (clientTaxCode is not null)
        {
            var buyerTaxCode = NormalizedFieldValue(document, nameof(FieldName.BuyerTaxCode));
            var supplierTaxCode = NormalizedFieldValue(document, nameof(FieldName.SupplierTaxCode));

            if (buyerTaxCode == clientTaxCode)
                direction = DocumentDirection.Purchase;
            else if (supplierTaxCode == clientTaxCode)
                direction = DocumentDirection.Sale;
        }

        document.Direction = direction;
        document.UpdatedAt = DateTime.UtcNow;

        // Recomputed on every call (unlike client auto-assignment, which never overwrites a
        // manual choice) so re-processing or a later manual client re-assignment is reflected —
        // the stale "unknown" warning from a prior run must not linger once resolved.
        foreach (var stale in document.ValidationWarnings.Where(w => w.WarningCode == DirectionUnknownWarningCode).ToList())
            _db.ValidationWarnings.Remove(stale);

        if (direction == DocumentDirection.Unknown)
        {
            _db.ValidationWarnings.Add(new ValidationWarning
            {
                DocumentId = documentId,
                WarningCode = DirectionUnknownWarningCode,
                Severity = ValidationSeverity.Info,
                Message = "Could not determine whether this is a purchase or a sale — please select it manually."
            });
        }

        await _db.SaveChangesAsync(ct);
        return direction;
    }

    private async Task<ClientProfile?> FindActiveClientAsync(Guid organizationId, string taxCode, CancellationToken ct) =>
        await _db.ClientProfiles.FirstOrDefaultAsync(
            c => c.OrganizationId == organizationId && c.IsActive && c.TaxCode == taxCode, ct);

    private string? NormalizedFieldValue(Document document, string fieldName)
    {
        var field = document.Fields.FirstOrDefault(f => f.FieldName == fieldName);
        return _normalization.NormalizeTaxCode(field?.NormalizedValue ?? field?.RawValue);
    }
}
