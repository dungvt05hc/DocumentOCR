using DocumentOCR.OcrBenchmark;
using Xunit;

namespace DocumentOCR.UnitTests.OcrBenchmark;

public class DeterministicDocumentIdTests
{
    [Fact]
    public void ForFileName_SameFileNameTwice_ReturnsSameGuid()
    {
        var first = DeterministicDocumentId.ForFileName("invoice1.pdf");
        var second = DeterministicDocumentId.ForFileName("invoice1.pdf");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ForFileName_DifferentFileNames_ReturnsDifferentGuids()
    {
        var first = DeterministicDocumentId.ForFileName("invoice1.pdf");
        var second = DeterministicDocumentId.ForFileName("invoice2.pdf");

        Assert.NotEqual(first, second);
    }
}
