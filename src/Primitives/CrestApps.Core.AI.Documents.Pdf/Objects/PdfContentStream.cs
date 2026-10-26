using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// One instruction of a content stream: an operator and the operands written before it.
/// </summary>
/// <param name="Name">The operator, for example <c>Tf</c>.</param>
/// <param name="Operands">The operands, as objects of the model; names keep their slash.</param>
internal sealed record PdfContentOperator(string Name, List<PdfItem> Operands)
{
    /// <summary>
    /// Gets the data of an inline image, for a <c>BI</c> operator whose operand is the image's dictionary.
    /// </summary>
    public byte[] InlineImageData { get; init; }
}

/// <summary>
/// Reads a content stream — the drawing instructions of a page or a form — into operators, and writes
/// operators back, so a tool can take some out and keep the rest exactly as it was.
/// </summary>
internal static class PdfContentStream
{
    /// <summary>
    /// Reads a content stream.
    /// </summary>
    /// <param name="content">The decoded stream.</param>
    /// <returns>The operators, in order.</returns>
    /// <exception cref="FormatException">The stream has a string, array or dictionary that never closes, or an inline image without its end.</exception>
    public static List<PdfContentOperator> Read(byte[] content)
    {
        var operators = new List<PdfContentOperator>();
        var operands = new List<PdfItem>();
        var position = 0;

        if (content is null)
        {
            return operators;
        }

        while (true)
        {
            var token = ReadToken(content, ref position, out var keyword);

            if (token is null && keyword is null)
            {
                return operators;
            }

            if (keyword is null)
            {
                operands.Add(token);

                continue;
            }

            if (keyword == "BI")
            {
                operators.Add(ReadInlineImage(content, ref position));
                operands = [];

                continue;
            }

            operators.Add(new PdfContentOperator(keyword, operands));
            operands = [];
        }
    }

    /// <summary>
    /// Writes operators as a content stream.
    /// </summary>
    /// <param name="operators">The operators.</param>
    /// <returns>The stream's bytes.</returns>
    public static byte[] Write(IEnumerable<PdfContentOperator> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);

        using var output = new MemoryStream();

        foreach (var instruction in operators)
        {
            if (instruction.Name == "BI" && instruction.InlineImageData is not null)
            {
                Append(output, "BI\n");

                if (instruction.Operands.Count > 0 && instruction.Operands[0] is PdfDictionary dictionary)
                {
                    foreach (var (key, value) in dictionary.Elements)
                    {
                        Append(output, PdfObjectWriter.ToSyntax(new PdfName(key)) + " " + PdfObjectWriter.ToSyntax(value) + "\n");
                    }
                }

                Append(output, "ID ");
                output.Write(instruction.InlineImageData);
                Append(output, "\nEI\n");

                continue;
            }

            foreach (var operand in instruction.Operands)
            {
                Append(output, PdfObjectWriter.ToSyntax(operand));
                output.WriteByte((byte)' ');
            }

            Append(output, instruction.Name);
            output.WriteByte((byte)'\n');
        }

        return output.ToArray();
    }

    private static void Append(MemoryStream output, string text)
    {
        output.Write(Encoding.Latin1.GetBytes(text));
    }

    /// <summary>
    /// Reads the next value, or the next keyword when it is not one of <c>true</c>, <c>false</c> and <c>null</c>.
    /// </summary>
    private static PdfItem ReadToken(byte[] content, ref int position, out string keyword)
    {
        keyword = null;
        SkipWhitespace(content, ref position);

        if (position >= content.Length)
        {
            return null;
        }

        var current = content[position];

        switch (current)
        {
            case (byte)'/':
                return ReadName(content, ref position);

            case (byte)'(':
                return ReadLiteral(content, ref position);

            case (byte)'<' when position + 1 < content.Length && content[position + 1] == (byte)'<':
                return ReadDictionary(content, ref position);

            case (byte)'<':
                return ReadHex(content, ref position);

            case (byte)'[':
                return ReadArray(content, ref position);

            case (byte)']' or (byte)'>' or (byte)')' or (byte)'{' or (byte)'}':
                // A stray closing mark is skipped, as viewers do.
                position++;

                return ReadToken(content, ref position, out keyword);
        }

        var start = position;

        while (position < content.Length && IsRegular(content[position]))
        {
            position++;
        }

        var word = Encoding.Latin1.GetString(content, start, position - start);

        if (TryNumber(word, out var number))
        {
            return number;
        }

        switch (word)
        {
            case "true":
                return PdfBoolean.True;

            case "false":
                return PdfBoolean.False;

            case "null":
                return PdfNull.Value;
        }

        keyword = word;

        return null;
    }

    private static PdfItem ReadValue(byte[] content, ref int position)
    {
        var value = ReadToken(content, ref position, out var keyword);

        if (value is null && keyword is null)
        {
            throw new FormatException("The content stream ends inside an array or a dictionary.");
        }

        // A keyword inside an array or dictionary is not valid; it is kept as a name so nothing is lost.
        return value ?? new PdfName("/" + keyword);
    }

    private static PdfName ReadName(byte[] content, ref int position)
    {
        var builder = new StringBuilder("/");

        position++;

        while (position < content.Length && IsRegular(content[position]))
        {
            if (content[position] == (byte)'#' && position + 2 < content.Length &&
                byte.TryParse(Encoding.ASCII.GetString(content, position + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var escaped))
            {
                builder.Append((char)escaped);
                position += 3;

                continue;
            }

            builder.Append((char)content[position]);
            position++;
        }

        return new PdfName(builder.ToString());
    }

    private static PdfString ReadLiteral(byte[] content, ref int position)
    {
        var bytes = new List<byte>();
        var depth = 1;

        position++;

        while (position < content.Length)
        {
            var current = content[position++];

            switch (current)
            {
                case (byte)'(':
                    depth++;
                    bytes.Add(current);

                    break;

                case (byte)')':
                    if (--depth == 0)
                    {
                        return new PdfString([.. bytes], isHex: false);
                    }

                    bytes.Add(current);

                    break;

                case (byte)'\\':
                    ReadEscape(content, ref position, bytes);

                    break;

                default:
                    bytes.Add(current);

                    break;
            }
        }

        throw new FormatException("The content stream has a string that never closes.");
    }

    private static void ReadEscape(byte[] content, ref int position, List<byte> bytes)
    {
        if (position >= content.Length)
        {
            return;
        }

        var current = content[position++];

        switch (current)
        {
            case (byte)'n':
                bytes.Add((byte)'\n');

                return;

            case (byte)'r':
                bytes.Add((byte)'\r');

                return;

            case (byte)'t':
                bytes.Add((byte)'\t');

                return;

            case (byte)'b':
                bytes.Add(8);

                return;

            case (byte)'f':
                bytes.Add(12);

                return;

            case (byte)'\r':
                // A backslash at the end of a line continues the string on the next one.
                if (position < content.Length && content[position] == (byte)'\n')
                {
                    position++;
                }

                return;

            case (byte)'\n':
                return;
        }

        if (current is >= (byte)'0' and <= (byte)'7')
        {
            var value = current - '0';

            for (var digit = 0; digit < 2 && position < content.Length && content[position] is >= (byte)'0' and <= (byte)'7'; digit++)
            {
                value = (value * 8) + (content[position++] - '0');
            }

            bytes.Add((byte)value);

            return;
        }

        bytes.Add(current);
    }

    private static PdfString ReadHex(byte[] content, ref int position)
    {
        var digits = new StringBuilder();

        position++;

        while (position < content.Length && content[position] != (byte)'>')
        {
            if (Uri.IsHexDigit((char)content[position]))
            {
                digits.Append((char)content[position]);
            }

            position++;
        }

        if (position >= content.Length)
        {
            throw new FormatException("The content stream has a hexadecimal string that never closes.");
        }

        position++;

        // An odd last digit stands for its value followed by zero.
        if (digits.Length % 2 == 1)
        {
            digits.Append('0');
        }

        return new PdfString(Convert.FromHexString(digits.ToString()), isHex: true);
    }

    private static PdfArray ReadArray(byte[] content, ref int position)
    {
        var array = new PdfArray(null);

        position++;

        while (true)
        {
            SkipWhitespace(content, ref position);

            if (position >= content.Length)
            {
                throw new FormatException("The content stream has an array that never closes.");
            }

            if (content[position] == (byte)']')
            {
                position++;

                return array;
            }

            array.Elements.Add(ReadValue(content, ref position));
        }
    }

    private static PdfDictionary ReadDictionary(byte[] content, ref int position)
    {
        var dictionary = new PdfDictionary(null);

        position += 2;

        while (true)
        {
            SkipWhitespace(content, ref position);

            if (position + 1 < content.Length && content[position] == (byte)'>' && content[position + 1] == (byte)'>')
            {
                position += 2;

                return dictionary;
            }

            if (position >= content.Length)
            {
                throw new FormatException("The content stream has a dictionary that never closes.");
            }

            if (ReadValue(content, ref position) is not PdfName key)
            {
                continue;
            }

            dictionary.Elements[key.Value] = ReadValue(content, ref position);
        }
    }

    private static PdfContentOperator ReadInlineImage(byte[] content, ref int position)
    {
        var dictionary = new PdfDictionary(null);

        while (true)
        {
            var token = ReadToken(content, ref position, out var keyword);

            if (keyword == "ID")
            {
                break;
            }

            if (token is null && keyword is null)
            {
                throw new FormatException("The content stream has an inline image without its data.");
            }

            if (token is PdfName key)
            {
                dictionary.Elements[key.Value] = ReadValue(content, ref position);
            }
        }

        // One white-space byte separates ID from the data.
        if (position < content.Length && IsWhitespace(content[position]))
        {
            position++;
        }

        var start = position;

        for (var index = start; index + 1 < content.Length; index++)
        {
            if (content[index] == (byte)'E' && content[index + 1] == (byte)'I' &&
                (index == start || IsWhitespace(content[index - 1])) &&
                (index + 2 >= content.Length || !IsRegular(content[index + 2])))
            {
                var end = index;

                if (end > start && IsWhitespace(content[end - 1]))
                {
                    end--;
                }

                position = index + 2;

                return new PdfContentOperator("BI", [dictionary]) { InlineImageData = content[start..end] };
            }
        }

        throw new FormatException("The content stream has an inline image without its end.");
    }

    private static bool TryNumber(string word, out PdfItem number)
    {
        number = null;

        if (word.Length == 0 || !(char.IsAsciiDigit(word[0]) || word[0] is '+' or '-' or '.'))
        {
            return false;
        }

        if (!word.Contains('.', StringComparison.Ordinal) && long.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
        {
            number = new PdfInteger(whole);

            return true;
        }

        if (double.TryParse(word, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var real))
        {
            number = new PdfReal(real);

            return true;
        }

        return false;
    }

    private static void SkipWhitespace(byte[] content, ref int position)
    {
        while (position < content.Length)
        {
            if (IsWhitespace(content[position]))
            {
                position++;

                continue;
            }

            if (content[position] == (byte)'%')
            {
                while (position < content.Length && content[position] is not ((byte)'\n' or (byte)'\r'))
                {
                    position++;
                }

                continue;
            }

            return;
        }
    }

    private static bool IsWhitespace(byte value)
    {
        return value is 0 or 9 or 10 or 12 or 13 or 32;
    }

    private static bool IsRegular(byte value)
    {
        return !IsWhitespace(value) && value is not ((byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%');
    }
}
