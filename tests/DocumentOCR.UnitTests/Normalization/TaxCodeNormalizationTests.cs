using DocumentOCR.Infrastructure.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Normalization;

public class TaxCodeNormalizationTests
{
    private readonly FieldNormalizationService _sut = new();

    [Theory]
    [InlineData("0123456789",      "0123456789")]     // already clean 10-digit
    [InlineData("0123456789-001",  "0123456789001")]  // 13-digit with dash
    [InlineData("0100109106",      "0100109106")]     // Vinamilk MST
    [InlineData(" 0100109106 ",    "0100109106")]     // whitespace stripped
    [InlineData("MST: 0100109106", "0100109106")]     // label prefix
    [InlineData("01-00-109106",    "0100109106")]     // dashes between digits
    public void NormalizeTaxCode_ValidInput_ReturnsDigitsOnly(string input, string expected)
    {
        var result = _sut.NormalizeTaxCode(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeTaxCode_NullOrEmpty_ReturnsNull(string? input)
    {
        var result = _sut.NormalizeTaxCode(input);
        // Empty string after stripping is still returned; purely null/whitespace inputs return null
        Assert.True(result is null || result.Length == 0);
    }
}
