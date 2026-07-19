namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// Configuration for the optional, free/open-source PaddleOCR provider. PaddleOCR itself runs
/// as a separate HTTP service (Docker container or local Python process, run by the caller) —
/// this class only configures how <see cref="PaddleOcrProvider"/> talks to it.
/// </summary>
public sealed class PaddleOcrOptions
{
    public const string SectionName = "PaddleOcr";

    /// <summary>Base URL of the PaddleOCR HTTP service (e.g. "http://localhost:8866").</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path appended to <see cref="BaseUrl"/> for the analyze request. The file is posted as
    /// multipart/form-data under the field name "file".
    /// </summary>
    public string AnalyzeEndpointPath { get; set; } = "/ocr/analyze";

    /// <summary>Total timeout for one analyze call, in seconds. Default: 60.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>True when the minimum required configuration (a base URL) is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
