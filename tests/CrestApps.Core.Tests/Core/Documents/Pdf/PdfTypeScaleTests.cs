using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfTypeScaleTests
{
    // Each product is a half of a tenth in decimal but not in binary floating point, where Math.Round(double, int)
    // rounds it one way on .NET 10 and the other on .NET 11. The test project runs on both, so these pin the sizes.
    [Theory]
    [InlineData(10.5, 1.9, 20)]
    [InlineData(10.5, 2.3, 24.2)]
    [InlineData(13, 1.05, 13.6)]
    [InlineData(10.5, 1.45, 15.2)]
    [InlineData(10.5, 0.6, 6.3)]
    [InlineData(12, 3, 36)]
    public void Scale_RoundsTheDecimalProductToATenth(double baseSize, double ratio, double expected)
    {
        Assert.Equal(expected, PdfTypeScale.Scale(baseSize, ratio));
    }
}
