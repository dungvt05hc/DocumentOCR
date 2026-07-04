using DocumentOCR.Infrastructure.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Normalization;

public class MoneyNormalizationTests
{
    private readonly FieldNormalizationService _sut = new();

    [Theory]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("1 234 567", 1234567)]
    [InlineData("1234567", 1234567)]
    [InlineData("1.234.567 VND", 1234567)]
    [InlineData("1,234,567 VND", 1234567)]
    [InlineData("1.234.567 VNĐ", 1234567)]
    [InlineData("₫1.234.567", 1234567)]
    [InlineData("VND 1.234.567", 1234567)]
    [InlineData("Tổng thanh toán: 1 234 567 VND", 1234567)]
    [InlineData("500.000", 500000)]
    [InlineData("500,000", 500000)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    public void NormalizeCurrency_VietnameseFormats_ReturnsCorrectValue(string input, decimal expected)
    {
        var result = _sut.NormalizeCurrency(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1.234.567,50", 1234567.5)]
    [InlineData("1.000,99", 1000.99)]
    [InlineData("123,45", 12345)]
    public void NormalizeCurrency_EuropeanFormat_ReturnsCorrectValue(string input, decimal expected)
    {
        var result = _sut.NormalizeCurrency(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1,234,567.50", 1234567.5)]
    [InlineData("1,000.99", 1000.99)]
    public void NormalizeCurrency_UsFormat_ReturnsCorrectValue(string input, decimal expected)
    {
        var result = _sut.NormalizeCurrency(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VND")]
    public void NormalizeCurrency_NullOrEmpty_ReturnsNull(string? input)
    {
        var result = _sut.NormalizeCurrency(input);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("VND")]
    [InlineData("VNĐ")]
    [InlineData("₫")]
    [InlineData("1.234.567 đồng")]
    public void NormalizeCurrencyCode_VietnameseMarkers_ReturnsVnd(string input)
    {
        var result = _sut.NormalizeCurrencyCode(input);
        Assert.Equal("VND", result);
    }
}
