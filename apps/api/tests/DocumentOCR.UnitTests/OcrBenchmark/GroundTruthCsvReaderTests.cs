using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class GroundTruthCsvReaderTests
{
    private const string Header =
        "FileName,DocumentCategory,DocumentSubType,ExpectedSupplierName,ExpectedSupplierTaxCode," +
        "ExpectedBuyerName,ExpectedBuyerTaxCode,ExpectedInvoiceNumber,ExpectedInvoiceDate," +
        "ExpectedSubtotalAmount,ExpectedVatAmount,ExpectedTotalAmount,ExpectedCurrency," +
        "QualityLevel,Notes";

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyDictionary()
    {
        var result = await GroundTruthCsvReader.LoadAsync(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-does-not-exist.csv"),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_WellFormedRow_ParsesAllColumns()
    {
        var csv = Header + "\n" +
            "mota-cafe.jpg,PosReceipt,SalesReceipt,MOTA CAFE,,,,111800005,2018-11-17," +
            "95000,,85000,VND,High,Clean scan";

        var path = await WriteTempCsvAsync(csv);
        try
        {
            var result = await GroundTruthCsvReader.LoadAsync(path, CancellationToken.None);

            var row = Assert.Single(result.Values);
            Assert.Equal("mota-cafe.jpg", row.FileName);
            Assert.Equal("PosReceipt", row.DocumentCategory);
            Assert.Equal("SalesReceipt", row.DocumentSubType);
            Assert.Equal("MOTA CAFE", row.ExpectedSupplierName);
            Assert.Null(row.ExpectedSupplierTaxCode);
            Assert.Equal("111800005", row.ExpectedInvoiceNumber);
            Assert.Equal("2018-11-17", row.ExpectedInvoiceDate);
            Assert.Equal("95000", row.ExpectedSubtotalAmount);
            Assert.Null(row.ExpectedVatAmount);
            Assert.Equal("85000", row.ExpectedTotalAmount);
            Assert.Equal("VND", row.ExpectedCurrency);
            Assert.Equal("High", row.QualityLevel);
            Assert.Equal("Clean scan", row.Notes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_LookupIsKeyedByFileNameCaseInsensitively()
    {
        var csv = Header + "\n" +
            "Invoice-ABC.PDF,VatInvoice,,CONG TY TNHH ABC,0100109106,,,0001234,2024-12-31," +
            "1030639,206128,1236767,VND,High,";

        var path = await WriteTempCsvAsync(csv);
        try
        {
            var result = await GroundTruthCsvReader.LoadAsync(path, CancellationToken.None);

            Assert.True(result.ContainsKey("invoice-abc.pdf"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_QuotedFieldWithEmbeddedComma_ParsesCorrectly()
    {
        var csv = Header + "\n" +
            "receipt.jpg,Receipt,,\"Minh An, Corp\",,,,RC-1,2024-01-01,,,,VND,Medium," +
            "\"Notes with, a comma and \"\"quotes\"\"\"";

        var path = await WriteTempCsvAsync(csv);
        try
        {
            var result = await GroundTruthCsvReader.LoadAsync(path, CancellationToken.None);

            var row = Assert.Single(result.Values);
            Assert.Equal("Minh An, Corp", row.ExpectedSupplierName);
            Assert.Equal("Notes with, a comma and \"quotes\"", row.Notes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MultipleRows_AllAreLoaded()
    {
        var csv = Header + "\n" +
            "a.pdf,VatInvoice,,A Co,0100109106,,,1,2024-01-01,1000,100,1100,VND,High,\n" +
            "b.jpg,PosReceipt,,B Cafe,,,,2,2024-01-02,2000,,2000,VND,High,";

        var path = await WriteTempCsvAsync(csv);
        try
        {
            var result = await GroundTruthCsvReader.LoadAsync(path, CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.Equal("A Co", result["a.pdf"].ExpectedSupplierName);
            Assert.Equal("B Cafe", result["b.jpg"].ExpectedSupplierName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteTempCsvAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-ground-truth.csv");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
