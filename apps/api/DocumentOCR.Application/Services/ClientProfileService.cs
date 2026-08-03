using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentOCR.Application.Services;

public class ClientProfileService
{
    private readonly IApplicationDbContext _db;
    private readonly IFieldNormalizationService _normalization;

    public ClientProfileService(IApplicationDbContext db, IFieldNormalizationService normalization)
    {
        _db = db;
        _normalization = normalization;
    }

    public async Task<List<ClientProfileDto>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
    {
        var clients = await _db.ClientProfiles
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return clients.Select(MapToDto).ToList();
    }

    public async Task<ClientProfileDto> CreateAsync(
        Guid organizationId, CreateClientProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required.");

        var taxCode = _normalization.NormalizeTaxCode(request.TaxCode);
        await EnsureTaxCodeNotTakenAsync(organizationId, taxCode, excludeClientId: null, ct);

        var client = new ClientProfile
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            TaxCode = taxCode,
            ClientType = request.ClientType,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            IsActive = true
        };

        _db.ClientProfiles.Add(client);
        await _db.SaveChangesAsync(ct);

        return MapToDto(client);
    }

    public async Task<ClientProfileDto> UpdateAsync(
        Guid id, Guid organizationId, UpdateClientProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required.");

        var client = await _db.ClientProfiles
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == organizationId, ct)
            ?? throw new KeyNotFoundException($"Client {id} not found.");

        var taxCode = _normalization.NormalizeTaxCode(request.TaxCode);
        await EnsureTaxCodeNotTakenAsync(organizationId, taxCode, excludeClientId: id, ct);

        client.Name = request.Name.Trim();
        client.TaxCode = taxCode;
        client.ClientType = request.ClientType;
        client.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        client.IsActive = request.IsActive;
        client.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MapToDto(client);
    }

    private async Task EnsureTaxCodeNotTakenAsync(
        Guid organizationId, string? taxCode, Guid? excludeClientId, CancellationToken ct)
    {
        if (taxCode is null) return;

        var taken = await _db.ClientProfiles.AnyAsync(
            c => c.OrganizationId == organizationId
                 && c.TaxCode == taxCode
                 && (excludeClientId == null || c.Id != excludeClientId),
            ct);

        if (taken)
            throw new ArgumentException("A client with this tax code already exists.");
    }

    private static ClientProfileDto MapToDto(ClientProfile c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        TaxCode = c.TaxCode,
        ClientType = c.ClientType,
        Address = c.Address,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };
}
