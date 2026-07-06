namespace DocumentOCR.Infrastructure.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder on the local file system where uploaded files are stored.</summary>
    public string BasePath { get; set; } = Path.Combine(Path.GetTempPath(), "DocumentOCR", "uploads");
}
