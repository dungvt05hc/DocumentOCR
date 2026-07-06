namespace DocumentOCR.Application.Models;

/// <summary>Encapsulates the input file data passed to <see cref="Interfaces.IDocumentOcrProvider"/>.</summary>
public sealed class DocumentInput
{
    /// <summary>Seekable stream containing the document bytes.</summary>
    public required Stream Content { get; init; }

    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSizeBytes { get; init; }
}
