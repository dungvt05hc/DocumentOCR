using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class CsvSummaryWriterTests
{
    private static BenchmarkCsvRow MakeRow(
        string fileName = "invoice1.pdf",
        string providerName = "Fake",
        string? errorMessage = null) => new(
        fileName, providerName, ModelId: "prebuilt-invoice", ProcessingDurationMs: 123.456,
        PageCount: 1, FullTextLength: 42, AverageConfidence: 0.9876,
        SupplierTaxCode: "0100109106", InvoiceDate: "2024-12-31", TotalAmount: "1236767",
        WarningCount: 2, ErrorMessage: errorMessage);

    [Fact]
    public async Task WriteAsync_WritesHeaderRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            await CsvSummaryWriter.WriteAsync(path, [MakeRow()], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Equal(
                "FileName,ProviderName,ModelId,ProcessingDurationMs,PageCount,FullTextLength," +
                "AverageConfidence,SupplierTaxCode,InvoiceDate,TotalAmount,WarningCount,ErrorMessage",
                lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_PlainRow_WritesExpectedValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            await CsvSummaryWriter.WriteAsync(path, [MakeRow()], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Equal(
                "invoice1.pdf,Fake,prebuilt-invoice,123.456,1,42,0.9876,0100109106,2024-12-31,1236767,2,",
                lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_ErrorMessageContainsCommaAndQuote_IsQuotedAndEscaped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var row = MakeRow(errorMessage: "Azure rejected the request (HTTP 400): \"bad, request\"");
            await CsvSummaryWriter.WriteAsync(path, [row], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.EndsWith(
                "\"Azure rejected the request (HTTP 400): \"\"bad, request\"\"\"",
                lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_NoRows_WritesOnlyHeader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            await CsvSummaryWriter.WriteAsync(path, [], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Single(lines);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
