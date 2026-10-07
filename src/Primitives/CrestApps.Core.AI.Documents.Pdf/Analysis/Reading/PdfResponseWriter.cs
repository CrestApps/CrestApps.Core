using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Builds a tool's answer line by line within a character budget, so a tool can stop at a boundary that
/// makes sense — the end of a page, a table or a match — and say what it left out, rather than being cut
/// mid-sentence.
/// </summary>
internal sealed class PdfResponseWriter
{
    // Room kept for the closing notes a tool always writes, such as how to ask for the rest.
    private const int Reserve = 600;

    private readonly StringBuilder _builder = new();
    private readonly int _limit;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfResponseWriter"/> class.
    /// </summary>
    /// <param name="limit">The most characters the answer may hold.</param>
    public PdfResponseWriter(int limit)
    {
        _limit = limit <= 0
            ? int.MaxValue
            : Math.Max(1_000, limit - Reserve);
    }

    /// <summary>
    /// Gets a value indicating whether a line has been refused for lack of room.
    /// </summary>
    public bool IsFull { get; private set; }

    /// <summary>
    /// Gets the number of characters written.
    /// </summary>
    public int Length => _builder.Length;

    /// <summary>
    /// Gets the number of characters that still fit.
    /// </summary>
    public int Remaining => IsFull ? 0 : Math.Max(0, _limit - _builder.Length);

    /// <summary>
    /// Returns whether text of a given length still fits.
    /// </summary>
    /// <param name="length">The number of characters.</param>
    /// <returns><see langword="true"/> when it fits.</returns>
    public bool Fits(int length)
    {
        return !IsFull && _builder.Length + length + 1 <= _limit;
    }

    /// <summary>
    /// Writes a line when it fits within the budget.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> when the line was written; once a line is refused, every later one is too.</returns>
    public bool TryLine(string line)
    {
        line ??= string.Empty;

        if (!Fits(line.Length))
        {
            IsFull = true;

            return false;
        }

        _builder.Append(line).Append('\n');

        return true;
    }

    /// <summary>
    /// Writes a line whatever the budget, for the notes that close an answer.
    /// </summary>
    /// <param name="line">The line.</param>
    public void Line(string line = "")
    {
        _builder.Append(line ?? string.Empty).Append('\n');
    }

    /// <summary>
    /// Returns the answer.
    /// </summary>
    /// <returns>The text written, without a trailing line break.</returns>
    public override string ToString()
    {
        return _builder.ToString().TrimEnd('\n');
    }
}
