using System.Security.Cryptography;
using System.Text;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Derives a stable Guid from a file name so the same sample file gets the same
/// <c>documentId</c> across both providers in a run — useful when diffing the two
/// providers' extracted-fields.json side by side. Not a security-sensitive hash,
/// just a correlation id, hence SHA256 over MD5 purely to avoid weak-hash lint noise.
/// </summary>
public static class DeterministicDocumentId
{
    public static Guid ForFileName(string fileName) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(fileName))[..16]);
}
