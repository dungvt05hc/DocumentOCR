using System.Text;
using DocumentOCR.Application.Processing;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.UnitTests.Parsing;

public class TT78XmlInvoiceParserTests
{
    private readonly TT78XmlInvoiceParser _sut = new();

    [Theory]
    [InlineData("valid-invoice.xml")]
    [InlineData("signed-invoice.xml")]
    public async Task ParseAsync_ValidTt78Xml_ExtractsAllFieldsWithFullConfidence(string fixtureFileName)
    {
        await using var stream = OpenFixture(fixtureFileName);

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(result.Fields, f => Assert.Equal(1.0, f.Confidence));
        Assert.All(result.Fields, f => Assert.Equal("TT78Xml", f.SourceType));
        Assert.All(result.Fields, f => Assert.Equal("TT78Xml", f.ExtractionMethod));

        AssertField(result, FieldName.InvoiceNumber, "00001234");
        AssertField(result, FieldName.InvoiceDate, "2026-07-15");
        AssertField(result, FieldName.SupplierTaxCode, "0100109106");
        AssertField(result, FieldName.SupplierName, "CÔNG TY TNHH ABC");
        AssertField(result, FieldName.SubtotalAmount, "1124334");
        AssertField(result, FieldName.VatAmount, "112433");
        AssertField(result, FieldName.TotalAmount, "1236767");
        AssertField(result, FieldName.Currency, "VND");
        AssertField(result, FieldName.Notes, "Mẫu số 1 - Ký hiệu C25TAA");
        AssertField(result, FieldName.DocumentType, nameof(DocumentType.VatInvoice));

        Assert.Equal("1", result.InvoiceTemplateCode);
        Assert.Equal("C25TAA", result.InvoiceSerial);
        Assert.False(string.IsNullOrWhiteSpace(result.RawXml));

        // The seller (NBan) fields must never be confused with the buyer (NMua) fields, even
        // though both carry the same "Ten"/"MST" local names.
        Assert.DoesNotContain(result.Fields, f => f.RawValue == "CÔNG TY TNHH XYZ");
        Assert.DoesNotContain(result.Fields, f => f.RawValue == "0109876543");
    }

    [Fact]
    public async Task ParseAsync_SignedInvoice_NeverReadsDataFromSignatureEnvelope()
    {
        // signed-invoice.xml deliberately contains a decoy <DLHDon><TTChung><SHDon>DECOY</SHDon>
        // nested inside the <Signature> block, positioned before the real data in document order.
        await using var stream = OpenFixture("signed-invoice.xml");

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        AssertField(result, FieldName.InvoiceNumber, "00001234");
        Assert.DoesNotContain(result.Fields, f => f.RawValue == "DECOY");
    }

    [Fact]
    public async Task ParseAsync_MissingOptionalFields_DoesNotThrowAndDefaultsCurrencyToVnd()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <HDon>
              <DLHDon>
                <TTChung>
                  <SHDon>00009999</SHDon>
                  <NLap>2026-07-15</NLap>
                </TTChung>
                <NDHDon>
                  <NBan>
                    <Ten>CÔNG TY TNHH ABC</Ten>
                    <MST>0100109106</MST>
                  </NBan>
                  <TToan>
                    <TgTTTBSo>1000000</TgTTTBSo>
                  </TToan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        await using var stream = ToStream(xml);

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        AssertField(result, FieldName.Currency, "VND");
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.Notes));
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.VatAmount));
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.SubtotalAmount));
        Assert.Null(result.InvoiceTemplateCode);
        Assert.Null(result.InvoiceSerial);
    }

    [Fact]
    public async Task ParseAsync_MalformedXml_ReturnsFailureWithoutThrowing()
    {
        await using var stream = ToStream("this is not xml at all <<<");

        var result = await _sut.ParseAsync(stream);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Empty(result.Fields);
    }

    [Fact]
    public async Task ParseAsync_XmlWithoutInvoiceDataBlock_ReturnsFailure()
    {
        await using var stream = ToStream("<Root><SomethingElse/></Root>");

        var result = await _sut.ParseAsync(stream);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Theory]
    [InlineData("text/xml", "invoice.xml", true)]
    [InlineData("application/xml", "invoice.xml", true)]
    [InlineData("application/pdf", "invoice.pdf", false)]
    public void CanParse_MatchesByContentTypeOrExtension(string contentType, string fileName, bool expected)
    {
        Assert.Equal(expected, _sut.CanParse(contentType, fileName));
    }

    private static void AssertField(
        Application.Models.StructuredInvoiceResult result, FieldName fieldName, string expectedRawValue)
    {
        var field = Assert.Single(result.Fields, f => f.FieldName == fieldName.ToString());
        Assert.Equal(expectedRawValue, field.RawValue);
    }

    private static Stream OpenFixture(string fileName) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tt78", fileName));

    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));
}
