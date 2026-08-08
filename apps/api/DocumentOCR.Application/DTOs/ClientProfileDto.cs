using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.DTOs;

public class ClientProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public ClientType ClientType { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateClientProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public ClientType ClientType { get; set; } = ClientType.HouseholdBusiness;
    public string? Address { get; set; }
}

public class UpdateClientProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public ClientType ClientType { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AssignDocumentClientRequest
{
    public Guid? ClientProfileId { get; set; }
}

public class SetDocumentDirectionRequest
{
    public DocumentDirection Direction { get; set; }
}
