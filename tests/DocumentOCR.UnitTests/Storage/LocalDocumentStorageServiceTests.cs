using DocumentOCR.Application.Exceptions;
using DocumentOCR.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Storage;

public class LocalDocumentStorageServiceTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "DocumentOCR-Tests", Guid.NewGuid().ToString());

    [Fact]
    public async Task SaveAsync_ValidFile_WritesUnderYearMonthFolderAndPreservesExtension()
    {
        var sut = CreateSut();
        using var content = new MemoryStream([1, 2, 3, 4]);

        var storedPath = await sut.SaveAsync(content, "invoice.pdf", "application/pdf");

        var expectedFolder = Path.Combine(DateTime.UtcNow.Year.ToString(), DateTime.UtcNow.Month.ToString("D2"));
        Assert.StartsWith(expectedFolder, storedPath);
        Assert.EndsWith(".pdf", storedPath);
        Assert.True(File.Exists(Path.Combine(_basePath, storedPath)));
    }

    [Fact]
    public async Task SaveAsync_ValidFile_ContentMatchesOriginalStream()
    {
        var sut = CreateSut();
        var bytes = new byte[] { 10, 20, 30, 40, 50 };
        using var content = new MemoryStream(bytes);

        var storedPath = await sut.SaveAsync(content, "receipt.png", "image/png");

        var savedBytes = await File.ReadAllBytesAsync(Path.Combine(_basePath, storedPath));
        Assert.Equal(bytes, savedBytes);
    }

    [Fact]
    public async Task GetStreamAsync_PreviouslySavedFile_ReturnsSameContent()
    {
        var sut = CreateSut();
        var bytes = new byte[] { 5, 6, 7 };
        using var content = new MemoryStream(bytes);
        var storedPath = await sut.SaveAsync(content, "invoice.pdf", "application/pdf");

        await using var stream = await sut.GetStreamAsync(storedPath);
        using var reader = new MemoryStream();
        await stream.CopyToAsync(reader);

        Assert.Equal(bytes, reader.ToArray());
    }

    [Fact]
    public async Task GetStreamAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            sut.GetStreamAsync(Path.Combine("2026", "07", "does-not-exist.pdf")));
    }

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("..\\..\\secrets.txt")]
    [InlineData("2026/../../../outside.pdf")]
    public async Task GetStreamAsync_PathEscapingStorageRoot_ThrowsPathTraversalException(string maliciousPath)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<PathTraversalException>(() => sut.GetStreamAsync(maliciousPath));
    }

    [Theory]
    [InlineData("../../../../etc/passwd")]
    [InlineData("..\\..\\secrets.txt")]
    public async Task DeleteAsync_PathEscapingStorageRoot_ThrowsPathTraversalException(string maliciousPath)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<PathTraversalException>(() => sut.DeleteAsync(maliciousPath));
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesIt()
    {
        var sut = CreateSut();
        using var content = new MemoryStream([1]);
        var storedPath = await sut.SaveAsync(content, "invoice.pdf", "application/pdf");

        await sut.DeleteAsync(storedPath);

        Assert.False(File.Exists(Path.Combine(_basePath, storedPath)));
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_DoesNotThrow()
    {
        var sut = CreateSut();

        await sut.DeleteAsync(Path.Combine("2026", "07", "does-not-exist.pdf"));
    }

    private LocalDocumentStorageService CreateSut()
    {
        var options = Options.Create(new StorageOptions { BasePath = _basePath });
        return new LocalDocumentStorageService(options, NullLogger<LocalDocumentStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
            Directory.Delete(_basePath, recursive: true);
    }
}
