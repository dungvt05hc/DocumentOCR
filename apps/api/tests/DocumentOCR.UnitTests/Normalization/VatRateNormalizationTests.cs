using DocumentOCR.Application.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Normalization;

public class VatRateNormalizationTests
{
    private readonly FieldNormalizationService _sut = new();

    [Theory]
    [InlineData("10%", "10%")]
    [InlineData("10 %", "10%")]
    [InlineData("10.0%", "10%")]
    [InlineData("10,0%", "10%")]
    [InlineData("0%", "0%")]
    [InlineData("5%", "5%")]
    [InlineData("8%", "8%")]
    [InlineData("Thuế suất 10%", "10%")]
    public void NormalizeVatRate_CanonicalPercentages_ReturnsCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, _sut.NormalizeVatRate(input));
    }

    [Theory]
    [InlineData("KCT")]
    [InlineData("kct")]
    [InlineData("Không chịu thuế")]
    public void NormalizeVatRate_NotSubjectToTax_ReturnsKct(string input)
    {
        Assert.Equal("KCT", _sut.NormalizeVatRate(input));
    }

    [Theory]
    [InlineData("KKKNT")]
    [InlineData("kkknt")]
    [InlineData("Không kê khai nộp thuế")]
    public void NormalizeVatRate_NotDeclared_ReturnsKkknt(string input)
    {
        Assert.Equal("KKKNT", _sut.NormalizeVatRate(input));
    }

    [Theory]
    [InlineData("12%")]
    [InlineData("15%")]
    [InlineData("abc")]
    public void NormalizeVatRate_UnrecognizedRate_ReturnsNull(string input)
    {
        Assert.Null(_sut.NormalizeVatRate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeVatRate_NullOrEmpty_ReturnsNull(string? input)
    {
        Assert.Null(_sut.NormalizeVatRate(input));
    }
}
