using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Services;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;
    private readonly IBackgroundJobClient _jobs;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly string[] AllowedContentTypes =
        ["application/pdf", "image/jpeg", "image/png"];

    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    public DocumentsController(DocumentService documentService, IBackgroundJobClient jobs)
    {
        _documentService = documentService;
        _jobs = jobs;
    }

    // POST /api/documents/upload
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes * 5)] // allow up to 5 files in one request
    public async Task<IActionResult> Upload(
        [FromForm] IFormFileCollection files,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        var results = new List<DocumentDto>();

        foreach (var file in files)
        {
            if (file.Length == 0)
                return BadRequest(new { error = $"File '{file.FileName}' is empty." });

            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { error = $"File '{file.FileName}' exceeds the 20 MB limit." });

            if (!AllowedContentTypes.Contains(file.ContentType.ToLower()))
                return BadRequest(new { error = $"File type '{file.ContentType}' is not supported. Allowed: PDF, JPEG, PNG." });

            await using var stream = file.OpenReadStream();
            var dto = await _documentService.UploadAsync(
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                DefaultOrganizationId,
                ct);

            // Immediately enqueue OCR processing as a background job
            _jobs.Enqueue<Application.Interfaces.IDocumentProcessingService>(
                svc => svc.ProcessAsync(dto.Id, CancellationToken.None));

            results.Add(dto);
        }

        return Ok(results);
    }

    // GET /api/documents
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var docs = await _documentService.GetAllAsync(DefaultOrganizationId, ct);
        return Ok(docs);
    }

    // GET /api/documents/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var doc = await _documentService.GetByIdAsync(id, ct);
        if (doc is null) return NotFound(new { error = $"Document {id} not found." });
        return Ok(doc);
    }

    // POST /api/documents/{id}/process  — re-trigger OCR
    [HttpPost("{id:guid}/process")]
    public async Task<IActionResult> Process(Guid id, CancellationToken ct)
    {
        await _documentService.EnqueueProcessingAsync(id, ct);
        return Accepted(new { message = "Processing enqueued." });
    }

    // PUT /api/documents/{id}/fields  — save user corrections
    [HttpPut("{id:guid}/fields")]
    public async Task<IActionResult> UpdateFields(
        Guid id,
        [FromBody] UpdateFieldsRequest request,
        CancellationToken ct)
    {
        if (request is null || request.Fields.Count == 0)
            return BadRequest(new { error = "No field updates provided." });

        await _documentService.UpdateFieldsAsync(id, request, ct);
        return NoContent();
    }

    // GET /api/documents/{id}/download-original
    [HttpGet("{id:guid}/download-original")]
    public async Task<IActionResult> DownloadOriginal(Guid id, CancellationToken ct)
    {
        var (stream, contentType, fileName) = await _documentService.DownloadOriginalAsync(id, ct);

        // Sanitize filename to prevent Content-Disposition injection
        var safeFileName = Path.GetFileName(fileName);
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{safeFileName}\"");
        return File(stream, contentType);
    }
}
