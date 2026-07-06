using DocumentOCR.Application.Exceptions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Storage;

/// <summary>
/// Stores uploaded documents on the local file system.
/// Each file is saved under {BasePath}/{year}/{month}/{newGuid}{ext} to avoid
/// enumerable paths and prevent directory traversal attacks.
/// </summary>
public class LocalDocumentStorageService : IDocumentStorageService
{
    private readonly StorageOptions _options;
    private readonly ILogger<LocalDocumentStorageService> _logger;

    public LocalDocumentStorageService(
        IOptions<StorageOptions> options,
        ILogger<LocalDocumentStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        CancellationToken ct = default)
    {
        // Build a safe, non-enumerable path
        var extension = Path.GetExtension(originalFileName);
        var relativePath = Path.Combine(
            DateTime.UtcNow.Year.ToString(),
            DateTime.UtcNow.Month.ToString("D2"),
            $"{Guid.NewGuid()}{extension}");

        var fullPath = Path.Combine(_options.BasePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileOutput = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
        await fileStream.CopyToAsync(fileOutput, ct);

        _logger.LogInformation("Saved file to {Path}", relativePath);
        return relativePath;
    }

    public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storedPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Stored file not found: {storedPath}");

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storedPath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    // Resolves relative path to absolute while preventing directory traversal
    private string ResolvePath(string storedPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_options.BasePath, storedPath));
        if (!fullPath.StartsWith(Path.GetFullPath(_options.BasePath), StringComparison.OrdinalIgnoreCase))
            throw new PathTraversalException("Access to files outside the storage root is not permitted.");
        return fullPath;
    }
}
