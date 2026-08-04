using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/exports")]
public class ExportsController : ControllerBase
{
    private readonly ExportService _exportService;
    private readonly IAccountingReportExportService _accountingReportExportService;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = DefaultOrganization.Id;

    public ExportsController(ExportService exportService, IAccountingReportExportService accountingReportExportService)
    {
        _exportService = exportService;
        _accountingReportExportService = accountingReportExportService;
    }

    // POST /api/exports/excel
    [HttpPost("excel")]
    [EnableRateLimiting("Export")]
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

    // POST /api/exports/accounting-report
    [HttpPost("accounting-report")]
    [EnableRateLimiting("Export")]
    public async Task<IActionResult> ExportAccountingReport(
        [FromBody] AccountingReportExportRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        if (request.To < request.From)
            return BadRequest(new { error = "'To' must not be earlier than 'From'." });

        var (bytes, fileName) = await _accountingReportExportService.ExportAsync(
            request.Type, request.ClientProfileId, request.From, request.To, DefaultOrganizationId, ct);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
