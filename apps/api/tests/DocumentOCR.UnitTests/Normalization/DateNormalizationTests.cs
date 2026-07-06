using DocumentOCR.Infrastructure.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Normalization;

public class DateNormalizationTests
{
    private readonly FieldNormalizationService _sut = new();

    // ── Common Vietnamese date formats ────────────────────────────────────────────

    [Theory]
    [InlineData("31/12/2024", 2024, 12, 31)]
    [InlineData("1/1/2024",   2024,  1,  1)]
    [InlineData("01/01/2024", 2024,  1,  1)]
    [InlineData("15/03/2023", 2023,  3, 15)]
    // dash separators
    [InlineData("31-12-2024", 2024, 12, 31)]
    [InlineData("01-01-2024", 2024,  1,  1)]
    // dot separators
    [InlineData("31.12.2024", 2024, 12, 31)]
    // ISO 8601
    [InlineData("2024-12-31", 2024, 12, 31)]
    [InlineData("2023-01-05", 2023,  1,  5)]
    [InlineData("Ngày hóa đơn: 05/06/2026", 2026, 6, 5)]
    // 2-digit year
    [InlineData("31/12/24",   2024, 12, 31)]
    public void NormalizeDate_ValidFormats_ReturnsParsedDate(
        string input, int year, int month, int day)
    {
        var result = _sut.NormalizeDate(input);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(year, month, day), result.Value);
    }

    // ── Null / invalid ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("99/99/9999")]
    public void NormalizeDate_InvalidOrEmpty_ReturnsNull(string? input)
    {
        var result = _sut.NormalizeDate(input);
        Assert.Null(result);
    }
}
