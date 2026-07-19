using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class GroundTruthComparerTests
{
    // ── Text matching (SupplierName / InvoiceNumber) ──────────────────────────────

    [Theory]
    [InlineData("CONG TY TNHH ABC", "cong ty tnhh abc")]
    [InlineData("  Cong Ty ABC  ", "cong ty   abc")]
    [InlineData("Cong Ty, ABC.", "Cong Ty ABC")]
    public void MatchText_NormalizedEquivalentValues_ReturnsTrue(string expected, string actual)
    {
        Assert.True(GroundTruthComparer.MatchText(expected, actual));
    }

    [Fact]
    public void MatchText_DifferentValues_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchText("CONG TY TNHH ABC", "CONG TY TNHH XYZ"));
    }

    [Fact]
    public void MatchText_EmptyExpected_ReturnsNullNotEvaluated()
    {
        Assert.Null(GroundTruthComparer.MatchText("", "anything"));
        Assert.Null(GroundTruthComparer.MatchText(null, "anything"));
    }

    [Fact]
    public void MatchText_InvoiceNumberWithDifferentPunctuation_ReturnsTrue()
    {
        Assert.True(GroundTruthComparer.MatchText("RC-9988", "RC 9988"));
    }

    [Fact]
    public void MatchText_DiacriticsOnlyDifference_ReturnsTrue()
    {
        // Ground truth typed without Vietnamese diacritics vs. OCR-extracted text with them —
        // a very common real-world shape for hand-entered ground truth CSVs.
        Assert.True(GroundTruthComparer.MatchText("CONG TY TNHH ABC", "CÔNG TY TNHH ABC"));
        Assert.True(GroundTruthComparer.MatchText("MOTA CAFE", "MOTA CAFE"));
        Assert.True(GroundTruthComparer.MatchText("Cua hang Minh An", "Cửa hàng Minh An"));
    }

    // ── Tax code matching (digits only) ────────────────────────────────────────────

    [Theory]
    [InlineData("0100109106", "0100109106")]
    [InlineData("010-0109-106", "0100109106")]
    [InlineData("MST: 0100109106", "0100109106")]
    public void MatchTaxCode_SameDigits_ReturnsTrue(string expected, string actual)
    {
        Assert.True(GroundTruthComparer.MatchTaxCode(expected, actual));
    }

    [Fact]
    public void MatchTaxCode_DifferentDigits_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchTaxCode("0100109106", "0100109107"));
    }

    [Fact]
    public void MatchTaxCode_EmptyExpected_ReturnsNullNotEvaluated()
    {
        Assert.Null(GroundTruthComparer.MatchTaxCode(null, "0100109106"));
    }

    [Fact]
    public void MatchTaxCode_ActualMissing_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchTaxCode("0100109106", null));
    }

    // ── Money matching (numeric value) ─────────────────────────────────────────────

    [Theory]
    [InlineData("1236767", "1.236.767")]
    [InlineData("1236767", "1,236,767")]
    [InlineData("1236767", "1 236 767")]
    [InlineData("1236767", "1.236.767 VND")]
    [InlineData("1236767", "₫1.236.767")]
    public void MatchMoney_EquivalentFormats_ReturnsTrue(string expected, string actual)
    {
        Assert.True(GroundTruthComparer.MatchMoney(expected, actual));
    }

    [Fact]
    public void MatchMoney_DifferentAmounts_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchMoney("1236767", "1236768"));
    }

    [Fact]
    public void MatchMoney_EmptyExpected_ReturnsNullNotEvaluated()
    {
        Assert.Null(GroundTruthComparer.MatchMoney(null, "1236767"));
    }

    [Fact]
    public void MatchMoney_ActualUnparsable_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchMoney("1236767", null));
    }

    // ── Date matching (ISO comparison) ─────────────────────────────────────────────

    [Theory]
    [InlineData("2018-11-17", "17/11/2018")]
    [InlineData("2018-11-17", "17/11/18")]
    [InlineData("2018-11-17", "2018-11-17")]
    [InlineData("2018-11-17", "17-11-2018")]
    public void MatchDate_EquivalentFormats_ReturnsTrue(string expected, string actual)
    {
        Assert.True(GroundTruthComparer.MatchDate(expected, actual));
    }

    [Fact]
    public void MatchDate_DifferentDates_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchDate("2018-11-17", "2018-11-18"));
    }

    [Fact]
    public void MatchDate_EmptyExpected_ReturnsNullNotEvaluated()
    {
        Assert.Null(GroundTruthComparer.MatchDate(null, "2018-11-17"));
    }

    // ── Currency matching ──────────────────────────────────────────────────────────

    [Fact]
    public void MatchCurrency_CaseInsensitiveMatch_ReturnsTrue()
    {
        Assert.True(GroundTruthComparer.MatchCurrency("VND", "vnd"));
    }

    [Fact]
    public void MatchCurrency_Mismatch_ReturnsFalse()
    {
        Assert.False(GroundTruthComparer.MatchCurrency("VND", "USD"));
    }

    [Fact]
    public void MatchCurrency_EmptyExpected_ReturnsNullNotEvaluated()
    {
        Assert.Null(GroundTruthComparer.MatchCurrency(null, "VND"));
    }

    // ── FieldAccuracyPercent ────────────────────────────────────────────────────────

    [Fact]
    public void CalculateFieldAccuracyPercent_AllMatched_Returns100()
    {
        var result = GroundTruthComparer.CalculateFieldAccuracyPercent(true, true, true);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void CalculateFieldAccuracyPercent_HalfMatched_Returns50()
    {
        var result = GroundTruthComparer.CalculateFieldAccuracyPercent(true, false, true, false);
        Assert.Equal(50.0, result);
    }

    [Fact]
    public void CalculateFieldAccuracyPercent_NullsIgnored_ExcludesFromDenominator()
    {
        // 2 evaluated (true, false), 2 not evaluated (null) — accuracy is 1/2, not 1/4.
        var result = GroundTruthComparer.CalculateFieldAccuracyPercent(true, false, null, null);
        Assert.Equal(50.0, result);
    }

    [Fact]
    public void CalculateFieldAccuracyPercent_NothingEvaluated_ReturnsNull()
    {
        var result = GroundTruthComparer.CalculateFieldAccuracyPercent(null, null, null);
        Assert.Null(result);
    }
}
