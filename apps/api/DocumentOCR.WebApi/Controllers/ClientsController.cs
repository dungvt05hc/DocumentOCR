using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly ClientProfileService _clientProfileService;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = DefaultOrganization.Id;

    public ClientsController(ClientProfileService clientProfileService)
    {
        _clientProfileService = clientProfileService;
    }

    // GET /api/clients
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var clients = await _clientProfileService.GetAllAsync(DefaultOrganizationId, ct);
        return Ok(clients);
    }

    // POST /api/clients
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientProfileRequest request, CancellationToken ct)
    {
        var client = await _clientProfileService.CreateAsync(DefaultOrganizationId, request, ct);
        return CreatedAtAction(nameof(GetAll), new { id = client.Id }, client);
    }

    // PUT /api/clients/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateClientProfileRequest request, CancellationToken ct)
    {
        var client = await _clientProfileService.UpdateAsync(id, DefaultOrganizationId, request, ct);
        return Ok(client);
    }
}
