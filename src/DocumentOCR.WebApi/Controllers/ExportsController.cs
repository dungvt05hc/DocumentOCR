using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/exports")]
public class ExportsController : ControllerBase
{
    private readonly ExportService _exportService;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public ExportsController(ExportService exportService)
    {
        _exportService = exportService;
    }

    // POST /api/exports/excel
    [HttpPost("excel")]
    public async Task<IActionResult> ExportExcel(
        [FromBody] ExportRequest request,
        CancellationToken ct)
    {
        if (request is null || request.DocumentIds.Count == 0)
            return BadRequest(new { error = "No document IDs provided." });

        var (bytes, fileName) = await _exportService.ExportToExcelAsync(request.DocumentIds, DefaultOrganizationId, ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
