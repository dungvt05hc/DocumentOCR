using DocumentOCR.Application.Processing;
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
    // English "dd-MMM-yyyy" (international invoices, e.g. Azure prebuilt-layout templates).
    // FieldExtractionService's own date regex is responsible for stripping any "Date:"/"Due
    // Date:" label before this ever sees the value — DateTime.TryParse's culture-aware fallback
    // handles the bare "dd-MMM-yyyy" shape fine, but not with a label prefix still attached.
    [InlineData("20-Mar-2008", 2008, 3, 20)]
    [InlineData("16-Oct-2016", 2016, 10, 16)]
    // Vietnamese textual date ("ngày X tháng Y năm Z") — the standard invoice-date wording.
    // No-space variant matches a PDF text layer that drops spaces between coordinate-positioned
    // glyphs (see the screenshot bug this covers: "31tháng07năm2026" from a real MISA e-invoice).
    [InlineData("31tháng07năm2026", 2026, 7, 31)]
    [InlineData("31 tháng 07 năm 2026", 2026, 7, 31)]
    [InlineData("Ngày 31 tháng 07 năm 2026", 2026, 7, 31)]
    [InlineData("ngày 5 tháng 6 năm 2026", 2026, 6, 5)]
    [InlineData("Ngày 05 Tháng 06 Năm 2026", 2026, 6, 5)]
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
    [InlineData("31 tháng 13 năm 2026")]
    [InlineData("31 tháng 02 năm 2026")]
    public void NormalizeDate_InvalidOrEmpty_ReturnsNull(string? input)
    {
        var result = _sut.NormalizeDate(input);
        Assert.Null(result);
    }
}
