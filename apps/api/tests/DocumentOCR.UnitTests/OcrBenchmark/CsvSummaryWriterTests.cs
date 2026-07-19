using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class CsvSummaryWriterTests
{
    private static BenchmarkCsvRow MakeRow(
        string fileName = "invoice1.pdf",
        string providerName = "Fake",
        string? errorMessage = null,
        bool? supplierNameMatched = true,
        double? fieldAccuracyPercent = 100.0) => new(
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
        ExpectedSupplierName: "CONG TY TNHH ABC",
        SupplierNameMatched: supplierNameMatched,
        ExtractedSupplierTaxCode: "0100109106",
        ExpectedSupplierTaxCode: "0100109106",
        TaxCodeMatched: true,
        ExtractedInvoiceNumber: "0001234",
        ExpectedInvoiceNumber: "0001234",
        InvoiceNumberMatched: true,
        ExtractedInvoiceDate: "2024-12-31",
        ExpectedInvoiceDate: "2024-12-31",
        InvoiceDateMatched: true,
        ExtractedSubtotalAmount: "1030639",
        ExpectedSubtotalAmount: "1030639",
        SubtotalMatched: true,
        ExtractedVatAmount: "206128",
        ExpectedVatAmount: "206128",
        VatMatched: true,
        ExtractedTotalAmount: "1236767",
        ExpectedTotalAmount: "1236767",
        TotalMatched: true,
        ExtractedCurrency: "VND",
        ExpectedCurrency: "VND",
        CurrencyMatched: true,
        FieldAccuracyPercent: fieldAccuracyPercent,
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
                "ExtractedSupplierName,ExpectedSupplierName,SupplierNameMatched," +
                "ExtractedSupplierTaxCode,ExpectedSupplierTaxCode,TaxCodeMatched," +
                "ExtractedInvoiceNumber,ExpectedInvoiceNumber,InvoiceNumberMatched," +
                "ExtractedInvoiceDate,ExpectedInvoiceDate,InvoiceDateMatched," +
                "ExtractedSubtotalAmount,ExpectedSubtotalAmount,SubtotalMatched," +
                "ExtractedVatAmount,ExpectedVatAmount,VatMatched," +
                "ExtractedTotalAmount,ExpectedTotalAmount,TotalMatched," +
                "ExtractedCurrency,ExpectedCurrency,CurrencyMatched," +
                "FieldAccuracyPercent," +
                "WarningCount,RawProviderResponsePath,NormalizedOcrResultPath,ErrorMessage",
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
                "3,1,5,0.9876,CÔNG TY TNHH ABC,CONG TY TNHH ABC,True," +
                "0100109106,0100109106,True," +
                "0001234,0001234,True," +
                "2024-12-31,2024-12-31,True," +
                "1030639,1030639,True," +
                "206128,206128,True," +
                "1236767,1236767,True," +
                "VND,VND,True," +
                "100.00," +
                "2,run/invoice1/Fake/raw-response.json,run/invoice1/Fake/ocr-result.json,",
                lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_NoGroundTruth_MatchColumnsAndAccuracyAreBlank()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var row = MakeRow(supplierNameMatched: null, fieldAccuracyPercent: null) with
            {
                ExpectedSupplierName = null,
                TaxCodeMatched = null,
                ExpectedSupplierTaxCode = null,
                InvoiceNumberMatched = null,
                ExpectedInvoiceNumber = null,
                InvoiceDateMatched = null,
                ExpectedInvoiceDate = null,
                SubtotalMatched = null,
                ExpectedSubtotalAmount = null,
                VatMatched = null,
                ExpectedVatAmount = null,
                TotalMatched = null,
                ExpectedTotalAmount = null,
                CurrencyMatched = null,
                ExpectedCurrency = null
            };
            await CsvSummaryWriter.WriteAsync(path, [row], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Equal(
                "invoice1.pdf,VatInvoice,Fake,prebuilt-invoice,keyValuePairs,123.456,1,42,8,30," +
                "3,1,5,0.9876,CÔNG TY TNHH ABC,,," +
                "0100109106,,," +
                "0001234,,," +
                "2024-12-31,,," +
                "1030639,,," +
                "206128,,," +
                "1236767,,," +
                "VND,,," +
                "," +
                "2,run/invoice1/Fake/raw-response.json,run/invoice1/Fake/ocr-result.json,",
                lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_MismatchedField_WritesFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var row = MakeRow(supplierNameMatched: false, fieldAccuracyPercent: 87.5);
            await CsvSummaryWriter.WriteAsync(path, [row], CancellationToken.None);
            var lines = await File.ReadAllLinesAsync(path);

            Assert.Contains(",CÔNG TY TNHH ABC,CONG TY TNHH ABC,False,", lines[1]);
            Assert.Contains(",87.50,", lines[1]);
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
