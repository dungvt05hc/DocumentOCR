using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Domain.Common;
using DocumentOCR.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/credits")]
public class CreditsController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ICreditService _creditService;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = DefaultOrganization.Id;

    public CreditsController(ICreditService creditService)
    {
        _creditService = creditService;
    }

    // GET /api/credits/balance
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        var balance = await _creditService.GetBalanceAsync(DefaultOrganizationId, ct);
        return Ok(new CreditBalanceDto { OrganizationId = DefaultOrganizationId, Balance = balance });
    }

    // GET /api/credits/transactions?page=&pageSize=
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, totalCount) = await _creditService.GetTransactionsAsync(DefaultOrganizationId, page, pageSize, ct);

        return Ok(new CreditTransactionPageDto
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items.Select(MapToDto).ToList()
        });
    }

    private static CreditTransactionDto MapToDto(CreditTransaction t) => new()
    {
        Id = t.Id,
        Type = t.Type,
        Amount = t.Amount,
        ReferenceType = t.ReferenceType,
        ReferenceId = t.ReferenceId,
        Description = t.Description,
        CreatedAt = t.CreatedAt
    };
}
