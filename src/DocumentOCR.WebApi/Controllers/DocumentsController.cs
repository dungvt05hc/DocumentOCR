using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Services;
using DocumentOCR.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<DocumentsController> _logger;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Dictionary<string, string[]> AllowedExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [".pdf"],
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/png"] = [".png"]
        };

    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    public DocumentsController(
        DocumentService documentService,
        IBackgroundJobClient jobs,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _jobs = jobs;
        _logger = logger;
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

        var results = new List<object>();

        foreach (var file in files)
        {
            var validationError = ValidateUpload(file);
            if (validationError is not null) return BadRequest(new { error = validationError });

            await using var stream = file.OpenReadStream();
            var dto = await _documentService.UploadAsync(
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                DefaultOrganizationId,
                ct);

            var jobId = _jobs.Enqueue<DocumentProcessingJob>(
                job => job.ProcessDocumentAsync(dto.Id));

            _logger.LogInformation(
                "Uploaded document {DocumentId} ({FileName}, {ContentType}, {FileSizeBytes} bytes) and enqueued Hangfire job {JobId}",
                dto.Id,
                dto.OriginalFileName,
                dto.ContentType,
                dto.FileSizeBytes,
                jobId);

            results.Add(new
            {
                dto.Id,
                DocumentId = dto.Id,
                dto.OriginalFileName,
                dto.ContentType,
                dto.FileSizeBytes,
                dto.PageCount,
                dto.Status,
                dto.DocumentType,
                dto.ErrorMessage,
                dto.ProcessingStartedAt,
                dto.ProcessingCompletedAt,
                dto.CreatedAt,
                dto.UpdatedAt,
                JobId = jobId,
                Message = "Document uploaded. OCR processing has been enqueued."
            });
        }

        return Accepted(results);
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
        await _documentService.MarkUploadedForProcessingAsync(id, ct);

        var jobId = _jobs.Enqueue<DocumentProcessingJob>(
            job => job.ProcessDocumentAsync(id));

        _logger.LogInformation(
            "Manually enqueued processing for document {DocumentId} as Hangfire job {JobId}",
            id,
            jobId);

        return Accepted(new { documentId = id, jobId, message = "Processing enqueued." });
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

    private static string? ValidateUpload(IFormFile file)
    {
        if (file.Length == 0)
            return $"File '{file.FileName}' is empty.";

        if (file.Length > MaxFileSizeBytes)
            return $"File '{file.FileName}' exceeds the 20 MB limit.";

        if (!AllowedExtensionsByContentType.TryGetValue(file.ContentType, out var allowedExtensions))
            return $"File type '{file.ContentType}' is not supported. Allowed: PDF, JPEG, PNG.";

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return $"File extension '{extension}' does not match content type '{file.ContentType}'.";
        }

        return null;
    }
}
