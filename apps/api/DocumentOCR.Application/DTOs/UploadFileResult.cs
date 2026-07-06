namespace DocumentOCR.Application.DTOs;

/// <summary>
/// Per-file outcome of a multi-file upload. Each file in a batch is validated and
/// persisted independently, so one invalid/failing file must not block or hide
/// the fact that other files in the same batch already succeeded.
/// </summary>
public class UploadFileResult
{
    public string FileName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>Populated only when <see cref="Success"/> is true.</summary>
    public DocumentDto? Document { get; set; }

    /// <summary>Hangfire job id for the enqueued processing job. Populated only when <see cref="Success"/> is true.</summary>
    public string? JobId { get; set; }
}
