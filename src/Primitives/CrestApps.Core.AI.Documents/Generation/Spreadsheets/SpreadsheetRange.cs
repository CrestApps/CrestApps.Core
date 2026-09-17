using System.Globalization;

namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// A parsed A1-notation range such as <c>A1:D20</c>.
/// <para>
/// Ranges reach this library as text written by a caller that is reasoning about a layout it cannot
/// see, so they are validated before use: an out-of-bounds merge or defined name makes the whole
/// workbook fail to open, which costs far more than the feature it expressed.
/// </para>
/// </summary>
public readonly struct SpreadsheetRange : IEquatable<SpreadsheetRange>
{
    private SpreadsheetRange(int firstColumnIndex, int firstRowNumber, int lastColumnIndex, int lastRowNumber)
    {
        FirstColumnIndex = firstColumnIndex;
        FirstRowNumber = firstRowNumber;
        LastColumnIndex = lastColumnIndex;
        LastRowNumber = lastRowNumber;
    }

    /// <summary>
    /// Gets the zero-based index of the leftmost column.
    /// </summary>
    public int FirstColumnIndex { get; }

    /// <summary>
    /// Gets the one-based number of the topmost row.
    /// </summary>
    public int FirstRowNumber { get; }

    /// <summary>
    /// Gets the zero-based index of the rightmost column.
    /// </summary>
    public int LastColumnIndex { get; }

    /// <summary>
    /// Gets the one-based number of the bottom row.
    /// </summary>
    public int LastRowNumber { get; }

    /// <summary>
    /// Gets a value indicating whether the range covers exactly one cell.
    /// </summary>
    public bool IsSingleCell => FirstColumnIndex == LastColumnIndex && FirstRowNumber == LastRowNumber;

    /// <summary>
    /// Gets the range in canonical A1 notation, with the corners ordered.
    /// </summary>
    public string Normalized =>
        $"{SpreadsheetFormula.ToCellReference(FirstColumnIndex, FirstRowNumber)}:{SpreadsheetFormula.ToCellReference(LastColumnIndex, LastRowNumber)}";

    /// <summary>
    /// Returns the range with absolute row and column markers, the form a defined name requires.
    /// </summary>
    /// <returns>The absolute range, for example <c>$A$1:$D$20</c>.</returns>
    public string ToAbsolute()
    {
        return $"${SpreadsheetFormula.ToColumnName(FirstColumnIndex)}${FirstRowNumber.ToString(CultureInfo.InvariantCulture)}" +
            $":${SpreadsheetFormula.ToColumnName(LastColumnIndex)}${LastRowNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Parses an A1-notation range or single cell reference.
    /// </summary>
    /// <param name="value">The range text, for example <c>A1:D20</c> or <c>B7</c>.</param>
    /// <param name="range">The parsed range.</param>
    /// <returns><see langword="true"/> when the value is a usable range.</returns>
    public static bool TryParse(string value, out SpreadsheetRange range)
    {
        range = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // A sheet qualifier is accepted and discarded; the writer supplies the sheet itself.
        var text = value.Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        var bang = text.LastIndexOf('!');

        if (bang >= 0)
        {
            text = text[(bang + 1)..];
        }

        var parts = text.Split(':');

        if (parts.Length > 2)
        {
            return false;
        }

        if (!TryParseCell(parts[0], out var firstColumn, out var firstRow))
        {
            return false;
        }

        var lastColumn = firstColumn;
        var lastRow = firstRow;

        if (parts.Length == 2 && !TryParseCell(parts[1], out lastColumn, out lastRow))
        {
            return false;
        }

        // The corners are ordered so a range written bottom-right first still works.
        range = new SpreadsheetRange(
            Math.Min(firstColumn, lastColumn),
            Math.Min(firstRow, lastRow),
            Math.Max(firstColumn, lastColumn),
            Math.Max(firstRow, lastRow));

        return true;
    }

    private static bool TryParseCell(string value, out int columnIndex, out int rowNumber)
    {
        columnIndex = 0;
        rowNumber = 0;

        var text = value.Trim();

        if (text.Length < 2)
        {
            return false;
        }

        var index = 0;
        var column = 0;

        while (index < text.Length && char.IsAsciiLetter(text[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(text[index]) - 'A' + 1);
            index++;

            // Past three letters the reference is beyond any real sheet and is almost certainly a typo.
            if (index > 3)
            {
                return false;
            }
        }

        if (index == 0 || index == text.Length)
        {
            return false;
        }

        for (var digit = index; digit < text.Length; digit++)
        {
            if (!char.IsAsciiDigit(text[digit]))
            {
                return false;
            }
        }

        if (!int.TryParse(text[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out rowNumber) ||
            rowNumber < 1)
        {
            return false;
        }

        columnIndex = column - 1;

        return true;
    }

    /// <summary>
    /// Determines whether this range equals another.
    /// </summary>
    /// <param name="other">The range to compare with.</param>
    public bool Equals(SpreadsheetRange other)
    {
        return FirstColumnIndex == other.FirstColumnIndex &&
            FirstRowNumber == other.FirstRowNumber &&
            LastColumnIndex == other.LastColumnIndex &&
            LastRowNumber == other.LastRowNumber;
    }

    /// <summary>
    /// Determines whether this range equals another object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    public override bool Equals(object obj)
    {
        return obj is SpreadsheetRange other && Equals(other);
    }

    /// <summary>
    /// Gets the hash code.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(FirstColumnIndex, FirstRowNumber, LastColumnIndex, LastRowNumber);
    }

    /// <summary>
    /// Returns the canonical A1 notation.
    /// </summary>
    public override string ToString()
    {
        return Normalized;
    }

    /// <summary>
    /// Determines whether two ranges are equal.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    public static bool operator ==(SpreadsheetRange left, SpreadsheetRange right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two ranges differ.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    public static bool operator !=(SpreadsheetRange left, SpreadsheetRange right)
    {
        return !left.Equals(right);
    }
}
