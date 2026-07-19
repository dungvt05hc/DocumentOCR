using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class CsvSummaryWriterTests
{
    private static BenchmarkCsvRow MakeRow(
        string fileName = "invoice1.pdf",
        string providerName = "Fake",
        string? errorMessage = null) => new(
        FileName: fileName,
        DocumentCategory: "VatInvoice",
        ProviderName: providerName,
        ModelId: "prebuilt-invoice",
        Features: "keyValuePairs",
        ProcessingDurationMs: 123.456,
        PageCount: 1,
        FullTextLength: 42,
        LineCount: 8,
        WordCount: 30,
        ParagraphCount: 3,
        TableCount: 1,
        KeyValuePairCount: 5,
        AverageConfidence: 0.9876,
        ExtractedSupplierName: "CÔNG TY TNHH ABC",
        ExtractedSupplierTaxCode: "0100109106",
        ExtractedInvoiceNumber: "0001234",
        ExtractedInvoiceDate: "2024-12-31",
        ExtractedSubtotalAmount: "1030639",
        ExtractedVatAmount: "206128",
        ExtractedTotalAmount: "1236767",
        ExtractedCurrency: "VND",
        WarningCount: 2,
        RawProviderResponsePath: "run/invoice1/Fake/raw-response.json",
        NormalizedOcrResultPath: "run/invoice1/Fake/ocr-result.json",
        ErrorMessage: errorMessage);

    [Fact]
    public async Task WriteAsync_WritesHeaderRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            await CsvSummaryWriter.WriteAsync(path, [MakeRow()], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Equal(
                "FileName,DocumentCategory,ProviderName,ModelId,Features," +
                "ProcessingDurationMs,PageCount,FullTextLength,LineCount,WordCount," +
                "ParagraphCount,TableCount,KeyValuePairCount,AverageConfidence," +
                "ExtractedSupplierName,ExtractedSupplierTaxCode,ExtractedInvoiceNumber," +
                "ExtractedInvoiceDate,ExtractedSubtotalAmount,ExtractedVatAmount," +
                "ExtractedTotalAmount,ExtractedCurrency,WarningCount," +
                "RawProviderResponsePath,NormalizedOcrResultPath,ErrorMessage",
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
                "invoice1.pdf,VatInvoice,Fake,prebuilt-invoice,keyValuePairs,123.456,1,42,8,30," +
                "3,1,5,0.9876,CÔNG TY TNHH ABC,0100109106,0001234,2024-12-31,1030639,206128," +
                "1236767,VND,2,run/invoice1/Fake/raw-response.json,run/invoice1/Fake/ocr-result.json,",
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
