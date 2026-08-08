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
        AssertField(result, FieldName.InvoiceForm, "1");
        AssertField(result, FieldName.InvoiceSymbol, "C25TAA");
        AssertField(result, FieldName.DocumentType, nameof(DocumentType.VatInvoice));

        Assert.Equal("1", result.InvoiceTemplateCode);
        Assert.Equal("C25TAA", result.InvoiceSerial);
        Assert.False(string.IsNullOrWhiteSpace(result.RawXml));

        // The seller (NBan) fields must never be confused with the buyer (NMua) fields, even
        // though both carry the same "Ten"/"MST" local names — each must land under its own
        // FieldName, not just "somewhere" in the result.
        AssertField(result, FieldName.SupplierName, "CÔNG TY TNHH ABC");
        AssertField(result, FieldName.SupplierTaxCode, "0100109106");
        AssertField(result, FieldName.BuyerName, "CÔNG TY TNHH XYZ");
        AssertField(result, FieldName.BuyerTaxCode, "0109876543");
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
    public async Task ParseAsync_RealMisaInvoice_ExtractsCorrectAmountsNotZero()
    {
        // Regression test for the bug where MISA's 6-decimal-place machine-formatted amounts
        // (e.g. "10019909.000000") were run through the Vietnamese-text money regex and came out
        // as 0 at full confidence — see docs/decisions.md. The fixture is the real
        // 1C26TNH-00000947 invoice XML that exposed the bug.
        await using var stream = OpenFixture("1C26TNH_00000947_4500609710.xml");

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);

        var subtotal = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.SubtotalAmount));
        var vat = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.VatAmount));
        var total = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.TotalAmount));

        Assert.Equal("10019909.000000", subtotal.RawValue);
        Assert.Equal("10019909", subtotal.NormalizedValue);
        Assert.Equal(1.0, subtotal.Confidence);

        Assert.Equal("1001991.000000", vat.RawValue);
        Assert.Equal("1001991", vat.NormalizedValue);
        Assert.Equal(1.0, vat.Confidence);

        Assert.Equal("11021900.000000", total.RawValue);
        Assert.Equal("11021900", total.NormalizedValue);
        Assert.Equal(1.0, total.Confidence);

        AssertField(result, FieldName.InvoiceForm, "1");
        AssertField(result, FieldName.InvoiceSymbol, "C26TNH");
        AssertField(result, FieldName.SupplierAddress, "Thôn Suối Vang, Xã Công Hải, tỉnh Khánh Hòa.");
        AssertField(result, FieldName.BuyerName, "CÔNG TY TNHH ĐẠT THỊNH THÀNH");
        AssertField(result, FieldName.BuyerTaxCode, "4500609710");
        AssertField(result, FieldName.BuyerAddress, "Tân Sơn 2, Phường Bảo An, Tỉnh Khánh Hòa, Việt Nam");
        AssertField(result, FieldName.TaxAuthorityCode, "00A6D0AC273E5944B3BCBB6DB672E020C8");
        AssertField(result, "PaymentMethod", "TM/CK");
        AssertField(result, "AmountInWords", "Mười một triệu không trăm hai mươi mốt nghìn chín trăm đồng.");
        AssertField(result, "LookupCode", "1ZFZUQ8W2Z38");

        // No <TCHDon> tag in this real sample — best-effort/unverified field, must stay absent
        // rather than fabricate a nature.
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNature));

        var taxLine = Assert.Single(result.TaxBreakdown);
        Assert.Equal("10%", taxLine.RawVatRate);
        Assert.Equal("10%", taxLine.VatRate);
        Assert.Equal(10019909.000000m, taxLine.TaxableAmount);
        Assert.Equal(1001991.000000m, taxLine.TaxAmount);
        Assert.Equal(1.0, taxLine.Confidence);
        Assert.Equal(0, taxLine.SortOrder);
    }

    [Fact]
    public async Task ParseAsync_MissingTToanBlock_MoneyFieldsAreAbsentNotZero()
    {
        // No <TToan> at all (not just a missing child tag inside it) — the totals are simply
        // unreadable. Per the "never default to 0 at full confidence" rule, no ExtractedField
        // should be created for any of the three money fields; the review layer then shows them
        // as missing (null/n-a) instead of a fabricated "0" at 100% confidence.
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
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        await using var stream = ToStream(xml);

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.SubtotalAmount));
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.VatAmount));
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.TotalAmount));
    }

    [Fact]
    public async Task ParseAsync_UnparseableMoneyTagContent_FieldIsOmittedNotZero()
    {
        // The tag exists but its content isn't a valid decimal (corrupt/garbled data) — treated
        // the same as the tag being absent: no field is fabricated at Confidence 1.0.
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
                    <TgTTTBSo>not-a-number</TgTTTBSo>
                  </TToan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        await using var stream = ToStream(xml);

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.TotalAmount));
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
        Assert.Empty(result.TaxBreakdown);
    }

    [Fact]
    public async Task ParseAsync_MultipleTaxRateLines_ProducesOneBreakdownRowPerRateInOrder()
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
                    <THTTLTSuat>
                      <LTSuat>
                        <TSuat>10%</TSuat>
                        <ThTien>1000000.000000</ThTien>
                        <TThue>100000.000000</TThue>
                      </LTSuat>
                      <LTSuat>
                        <TSuat>KCT</TSuat>
                        <ThTien>500000.000000</ThTien>
                        <TThue>0.000000</TThue>
                      </LTSuat>
                    </THTTLTSuat>
                    <TgTTTBSo>1600000</TgTTTBSo>
                  </TToan>
                </NDHDon>
              </DLHDon>
            </HDon>
            """;
        await using var stream = ToStream(xml);

        var result = await _sut.ParseAsync(stream);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.TaxBreakdown.Count);

        Assert.Equal("10%", result.TaxBreakdown[0].VatRate);
        Assert.Equal(1000000m, result.TaxBreakdown[0].TaxableAmount);
        Assert.Equal(100000m, result.TaxBreakdown[0].TaxAmount);
        Assert.Equal(0, result.TaxBreakdown[0].SortOrder);

        Assert.Equal("KCT", result.TaxBreakdown[1].VatRate);
        Assert.Equal(500000m, result.TaxBreakdown[1].TaxableAmount);
        Assert.Equal(0m, result.TaxBreakdown[1].TaxAmount);
        Assert.Equal(1, result.TaxBreakdown[1].SortOrder);
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
        Application.Models.StructuredInvoiceResult result, FieldName fieldName, string expectedRawValue) =>
        AssertField(result, fieldName.ToString(), expectedRawValue);

    private static void AssertField(
        Application.Models.StructuredInvoiceResult result, string fieldName, string expectedRawValue)
    {
        var field = Assert.Single(result.Fields, f => f.FieldName == fieldName);
        Assert.Equal(expectedRawValue, field.RawValue);
    }

    private static Stream OpenFixture(string fileName) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tt78", fileName));

    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));
}
