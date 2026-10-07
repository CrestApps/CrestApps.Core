using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One operation of a content stream: an operator and the operands written before it.
/// </summary>
/// <param name="Operator">The operator, for example <c>Tf</c>.</param>
/// <param name="Operands">The operands at the top level: names keep their slash (<c>/F1</c>), numbers and keywords are written as they appear, and strings, arrays and dictionaries are written as <c>()</c>, <c>[]</c> and <c>&lt;&lt;&gt;&gt;</c>.</param>
internal sealed record PdfContentOperation(string Operator, List<string> Operands);

/// <summary>
/// What reading a content stream found.
/// </summary>
internal sealed class PdfContentParseResult
{
    /// <summary>
    /// Gets the operations, in the order they are drawn.
    /// </summary>
    public List<PdfContentOperation> Operations { get; } = [];

    /// <summary>
    /// Gets the syntax errors: text a reader cannot make sense of.
    /// </summary>
    public List<string> Errors { get; } = [];

    /// <summary>
    /// Gets the problems that do not stop the stream from being read, such as unknown operators.
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether reading stopped at the operation limit.
    /// </summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Reads PDF content streams — the drawing instructions of pages and form XObjects — and checks their syntax.
/// </summary>
/// <remarks>
/// PDFsharp's content reader is not used for this: given a string that never closes it grows its buffer until
/// the process runs out of memory, and it fails on an operator it does not know. This reader keeps only what
/// the checks need (operators, and the names they refer to), never allocates more than the stream's own size,
/// and reports what it could not read instead of throwing.
/// </remarks>
internal static class PdfContentParser
{
    private const int MaxMessages = 20;

    // The operators of ISO 32000 with the number of operands each takes; -1 is a variable number.
    private static readonly Dictionary<string, int> _operators = new(StringComparer.Ordinal)
    {
        ["b"] = 0, ["B"] = 0, ["b*"] = 0, ["B*"] = 0, ["BDC"] = 2, ["BI"] = 0, ["BMC"] = 1, ["BT"] = 0, ["BX"] = 0,
        ["c"] = 6, ["cm"] = 6, ["CS"] = 1, ["cs"] = 1, ["d"] = 2, ["d0"] = 2, ["d1"] = 6, ["Do"] = 1, ["DP"] = 2,
        ["EI"] = 0, ["EMC"] = 0, ["ET"] = 0, ["EX"] = 0, ["f"] = 0, ["F"] = 0, ["f*"] = 0, ["G"] = 1, ["g"] = 1,
        ["gs"] = 1, ["h"] = 0, ["i"] = 1, ["ID"] = 0, ["j"] = 1, ["J"] = 1, ["K"] = 4, ["k"] = 4, ["l"] = 2,
        ["m"] = 2, ["M"] = 1, ["MP"] = 1, ["n"] = 0, ["q"] = 0, ["Q"] = 0, ["re"] = 4, ["RG"] = 3, ["rg"] = 3,
        ["ri"] = 1, ["s"] = 0, ["S"] = 0, ["SC"] = -1, ["sc"] = -1, ["SCN"] = -1, ["scn"] = -1, ["sh"] = 1,
        ["T*"] = 0, ["Tc"] = 1, ["Td"] = 2, ["TD"] = 2, ["Tf"] = 2, ["Tj"] = 1, ["TJ"] = 1, ["TL"] = 1, ["Tm"] = 6,
        ["Tr"] = 1, ["Ts"] = 1, ["Tw"] = 1, ["Tz"] = 1, ["v"] = 4, ["w"] = 1, ["W"] = 0, ["W*"] = 0, ["y"] = 4,
        ["'"] = 1, ["\""] = 3,
    };

    private static readonly HashSet<string> _textShowing = new(StringComparer.Ordinal) { "Tj", "TJ", "'", "\"" };

    /// <summary>
    /// Returns whether an operator shows text.
    /// </summary>
    /// <param name="name">The operator.</param>
    /// <returns><see langword="true"/> for <c>Tj</c>, <c>TJ</c>, <c>'</c> and <c>"</c>.</returns>
    public static bool IsTextShowing(string name)
    {
        return _textShowing.Contains(name);
    }

    /// <summary>
    /// Reads a content stream.
    /// </summary>
    /// <param name="content">The decoded stream.</param>
    /// <param name="maxOperations">The most operations read.</param>
    /// <returns>What was found.</returns>
    public static PdfContentParseResult Parse(byte[] content, int maxOperations = 500_000)
    {
        var result = new PdfContentParseResult();

        if (content is null || content.Length == 0)
        {
            return result;
        }

        var operands = new List<string>();
        var position = 0;
        var nesting = 0;
        var compatibility = 0;
        var saves = 0;
        var inText = false;
        var unbalancedRestores = 0;
        var textOutsideObject = 0;

        while (position < content.Length)
        {
            var current = content[position];

            if (IsWhitespace(current))
            {
                position++;

                continue;
            }

            switch (current)
            {
                case (byte)'%':
                    while (position < content.Length && content[position] is not ((byte)'\n' or (byte)'\r'))
                    {
                        position++;
                    }

                    continue;
                case (byte)'(':
                    if (!SkipLiteralString(content, ref position))
                    {
                        AddError(result, $"a string that is never closed starts at byte {position:N0}");

                        return result;
                    }

                    AddOperand(operands, nesting, "()");

                    continue;
                case (byte)'<' when position + 1 < content.Length && content[position + 1] == (byte)'<':
                    position += 2;

                    if (nesting == 0)
                    {
                        operands.Add("<<>>");
                    }

                    nesting++;

                    continue;
                case (byte)'<':
                    var hexStart = position;

                    if (!SkipHexString(content, ref position, out var invalid))
                    {
                        AddError(result, $"a hexadecimal string that is never closed starts at byte {hexStart:N0}");

                        return result;
                    }

                    if (invalid)
                    {
                        AddError(result, $"the hexadecimal string at byte {hexStart:N0} holds characters that are not hexadecimal digits");
                    }

                    AddOperand(operands, nesting, "()");

                    continue;
                case (byte)'>' when position + 1 < content.Length && content[position + 1] == (byte)'>':
                    position += 2;

                    if (nesting == 0)
                    {
                        AddError(result, $"a dictionary closes at byte {position - 2:N0} that was never opened");
                    }
                    else
                    {
                        nesting--;
                    }

                    continue;
                case (byte)'[':
                    position++;

                    if (nesting == 0)
                    {
                        operands.Add("[]");
                    }

                    nesting++;

                    continue;
                case (byte)']':
                    position++;

                    if (nesting == 0)
                    {
                        AddError(result, $"an array closes at byte {position - 1:N0} that was never opened");
                    }
                    else
                    {
                        nesting--;
                    }

                    continue;
                case (byte)'>' or (byte)')' or (byte)'{' or (byte)'}':
                    AddError(result, $"unexpected '{(char)current}' at byte {position:N0}");
                    position++;

                    continue;
                case (byte)'/':
                    var nameStart = position;
                    position++;

                    while (position < content.Length && IsRegular(content[position]))
                    {
                        position++;
                    }

                    AddOperand(operands, nesting, Encoding.Latin1.GetString(content, nameStart, position - nameStart));

                    continue;
            }

            var tokenStart = position;

            while (position < content.Length && IsRegular(content[position]))
            {
                position++;
            }

            var token = Encoding.Latin1.GetString(content, tokenStart, position - tokenStart);

            if (IsNumber(token) || token is "true" or "false" or "null")
            {
                AddOperand(operands, nesting, token);

                continue;
            }

            if (nesting > 0)
            {
                AddError(result, $"an array or dictionary is still open when the operator '{Shorten(token)}' is reached at byte {tokenStart:N0}");
                nesting = 0;
            }

            if (token == "BI")
            {
                if (!SkipInlineImage(content, ref position))
                {
                    AddError(result, $"the inline image at byte {tokenStart:N0} has no end (EI)");

                    return result;
                }

                result.Operations.Add(new PdfContentOperation("BI", []));
                operands.Clear();

                continue;
            }

            if (_operators.TryGetValue(token, out var arity))
            {
                if (arity >= 0 && operands.Count != arity && compatibility == 0)
                {
                    AddWarning(result, $"the operator '{token}' at byte {tokenStart:N0} has {operands.Count} operand(s) where {arity} are expected");
                }
            }
            else if (compatibility == 0)
            {
                AddWarning(result, $"'{Shorten(token)}' at byte {tokenStart:N0} is not a PDF operator");
            }

            switch (token)
            {
                case "BX":
                    compatibility++;

                    break;
                case "EX":
                    compatibility = Math.Max(0, compatibility - 1);

                    break;
                case "q":
                    saves++;

                    break;
                case "Q":
                    if (saves == 0)
                    {
                        unbalancedRestores++;
                    }
                    else
                    {
                        saves--;
                    }

                    break;
                case "BT":
                    if (inText)
                    {
                        AddWarning(result, $"a text object begins at byte {tokenStart:N0} inside another one (BT without ET)");
                    }

                    inText = true;

                    break;
                case "ET":
                    if (!inText)
                    {
                        AddWarning(result, $"a text object ends at byte {tokenStart:N0} that never began (ET without BT)");
                    }

                    inText = false;

                    break;
                default:
                    if (!inText && IsTextShowing(token))
                    {
                        textOutsideObject++;
                    }

                    break;
            }

            result.Operations.Add(new PdfContentOperation(token, [.. operands]));
            operands.Clear();

            if (result.Operations.Count >= maxOperations)
            {
                result.Truncated = true;

                break;
            }
        }

        if (nesting > 0)
        {
            AddError(result, "the stream ends inside an array or dictionary");
        }

        if (inText)
        {
            AddWarning(result, "the stream ends inside a text object (BT without ET)");
        }

        if (unbalancedRestores > 0)
        {
            AddWarning(result, string.Create(CultureInfo.InvariantCulture, $"the graphics state is restored {unbalancedRestores} time(s) more than it is saved (Q without q)"));
        }

        if (textOutsideObject > 0)
        {
            AddWarning(result, string.Create(CultureInfo.InvariantCulture, $"text is shown {textOutsideObject} time(s) outside a text object (BT/ET)"));
        }

        return result;
    }

    private static void AddOperand(List<string> operands, int nesting, string operand)
    {
        if (nesting == 0)
        {
            operands.Add(operand);
        }
    }

    private static void AddError(PdfContentParseResult result, string message)
    {
        if (result.Errors.Count < MaxMessages)
        {
            result.Errors.Add(message);
        }
    }

    private static void AddWarning(PdfContentParseResult result, string message)
    {
        if (result.Warnings.Count < MaxMessages)
        {
            result.Warnings.Add(message);
        }
    }

    private static string Shorten(string token)
    {
        return token.Length > 20
            ? token[..20] + "…"
            : token;
    }

    private static bool SkipLiteralString(byte[] content, ref int position)
    {
        var depth = 0;

        while (position < content.Length)
        {
            var current = content[position++];

            if (current == (byte)'\\')
            {
                position++;

                continue;
            }

            if (current == (byte)'(')
            {
                depth++;
            }
            else if (current == (byte)')')
            {
                depth--;

                if (depth == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SkipHexString(byte[] content, ref int position, out bool invalid)
    {
        invalid = false;
        position++;

        while (position < content.Length)
        {
            var current = content[position++];

            if (current == (byte)'>')
            {
                return true;
            }

            if (!IsWhitespace(current) && !char.IsAsciiHexDigit((char)current))
            {
                invalid = true;
            }
        }

        return false;
    }

    private static bool SkipInlineImage(byte[] content, ref int position)
    {
        // The image dictionary runs to ID; the data after it runs to an EI that stands alone.
        var id = Find(content, position, "ID");

        if (id < 0)
        {
            return false;
        }

        var index = id + 3;

        while (index + 1 < content.Length)
        {
            if (content[index] == (byte)'E' &&
                content[index + 1] == (byte)'I' &&
                IsWhitespace(content[index - 1]) &&
                (index + 2 == content.Length || IsWhitespace(content[index + 2]) || IsDelimiter(content[index + 2])))
            {
                position = index + 2;

                return true;
            }

            index++;
        }

        return false;
    }

    private static int Find(byte[] content, int start, string token)
    {
        for (var index = start; index + token.Length <= content.Length; index++)
        {
            var matches = true;

            for (var offset = 0; offset < token.Length; offset++)
            {
                if (content[index + offset] != (byte)token[offset])
                {
                    matches = false;

                    break;
                }
            }

            if (matches &&
                (index == 0 || !IsRegular(content[index - 1])) &&
                (index + token.Length == content.Length || !IsRegular(content[index + token.Length])))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsNumber(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        var digits = 0;
        var dots = 0;

        for (var index = 0; index < token.Length; index++)
        {
            var character = token[index];

            if (char.IsAsciiDigit(character))
            {
                digits++;
            }
            else if (character == '.')
            {
                dots++;
            }
            else if (character is not ('+' or '-') || index > 0)
            {
                return false;
            }
        }

        return digits > 0 && dots <= 1;
    }

    private static bool IsWhitespace(byte value)
    {
        return value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
    }

    private static bool IsDelimiter(byte value)
    {
        return value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
    }

    private static bool IsRegular(byte value)
    {
        return !IsWhitespace(value) && !IsDelimiter(value);
    }
}
