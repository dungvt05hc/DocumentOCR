namespace DocumentOCR.Application.Interfaces;

/// <summary>Abstraction over file storage. Swap Local ↔ Azure Blob by changing the registration.</summary>
public interface IDocumentStorageService
{
    /// <summary>Persists a file and returns the stored path/key.</summary>
    Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default);

    /// <summary>Opens a read-only stream for a previously stored file.</summary>
    Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default);

    /// <summary>Deletes the stored file. Silently succeeds if file does not exist.</summary>
    Task DeleteAsync(string storedPath, CancellationToken ct = default);
}
