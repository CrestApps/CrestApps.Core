namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Derives the theme's type sizes and spacing from its base font size.
/// </summary>
/// <remarks>
/// The ratios land on halves of a tenth for common base sizes (10.5pt × 1.9 is 19.95pt), and in binary
/// floating point those products sit a hair under or over the half. <see cref="Math.Round(double, int)"/>
/// rounds them differently on different runtimes (.NET 10 rounds 10.5 × 1.9 to 20, .NET 11 to 19.9), so
/// the same definition would lay out differently depending on where it runs. Multiplying and rounding in
/// <see cref="decimal"/> keeps the product exact, so every target framework produces the same document.
/// The conversion from <see cref="double"/> changed too (.NET 10 keeps 15 significant digits, so 1.9 becomes
/// 1.9; .NET 11 keeps the exact binary value, 1.899999999999999911...), so each input is first rounded to ten
/// decimal places, which gives back the value as written on both.
/// </remarks>
internal static class PdfTypeScale
{
    /// <summary>
    /// Scales a base size by a ratio and rounds the result to a tenth of a point.
    /// </summary>
    /// <param name="baseSize">The base size, in points.</param>
    /// <param name="ratio">The ratio to scale by.</param>
    /// <returns>The scaled size, rounded to a tenth of a point with halves going to the even tenth.</returns>
    public static double Scale(double baseSize, double ratio)
    {
        return (double)Math.Round(ToDecimal(baseSize) * ToDecimal(ratio), 1, MidpointRounding.ToEven);
    }

    private static decimal ToDecimal(double value)
    {
        return Math.Round((decimal)value, 10, MidpointRounding.ToEven);
    }
}
