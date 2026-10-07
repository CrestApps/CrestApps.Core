using System.Globalization;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Evaluates the calculated-column formulas a tabular export records, such as <c>={Actual}-{Planned}</c>,
/// against one row's values.
/// </summary>
/// <remarks>
/// A workbook writes such a column as a live formula and lets the spreadsheet compute it. A PDF has no one to
/// compute it, so without this a calculated column prints empty. The supported vocabulary is what those
/// formulas are written in: arithmetic, comparisons, text joining with <c>&amp;</c>, and <c>IF</c>,
/// <c>IFERROR</c>, <c>ABS</c>, <c>ROUND</c>, <c>ROUNDUP</c>, <c>ROUNDDOWN</c>, <c>MIN</c>, <c>MAX</c>,
/// <c>SUM</c>, <c>AVERAGE</c>, <c>AND</c>, <c>OR</c> and <c>NOT</c>. Anything else evaluates to an error
/// value printed the way a spreadsheet prints it, rather than to a wrong number.
/// </remarks>
internal sealed class PdfFormulaEvaluator
{
    private readonly string _text;
    private readonly Func<string, string> _resolveColumn;
    private readonly int _rowNumber;
    private int _position;

    private PdfFormulaEvaluator(string text, Func<string, string> resolveColumn, int rowNumber)
    {
        _text = text;
        _resolveColumn = resolveColumn;
        _rowNumber = rowNumber;
    }

    /// <summary>
    /// Evaluates a formula.
    /// </summary>
    /// <param name="formula">The formula, with or without its leading <c>=</c>.</param>
    /// <param name="resolveColumn">Returns a column's value in the current row by name, or <see langword="null"/> when there is no such column.</param>
    /// <param name="rowNumber">The spreadsheet row number, which <c>{row}</c> stands for.</param>
    /// <returns>The result as cell text: a number in invariant form, text, <c>TRUE</c>/<c>FALSE</c>, or an error such as <c>#DIV/0!</c>.</returns>
    public static string Evaluate(string formula, Func<string, string> resolveColumn, int rowNumber)
    {
        ArgumentNullException.ThrowIfNull(resolveColumn);

        if (string.IsNullOrWhiteSpace(formula))
        {
            return string.Empty;
        }

        var text = formula.Trim();

        if (text.StartsWith('='))
        {
            text = text[1..];
        }

        var evaluator = new PdfFormulaEvaluator(text, resolveColumn, rowNumber);

        try
        {
            var value = evaluator.ParseComparison();
            evaluator.SkipWhitespace();

            if (evaluator._position < text.Length)
            {
                return "#NAME?";
            }

            return value.ToText();
        }
        catch (FormatException)
        {
            return "#NAME?";
        }
    }

    private Value ParseComparison()
    {
        var left = ParseConcatenation();

        while (true)
        {
            SkipWhitespace();

            var op = ReadComparisonOperator();

            if (op is null)
            {
                return left;
            }

            var right = ParseConcatenation();

            if (left.IsError)
            {
                return left;
            }

            if (right.IsError)
            {
                return right;
            }

            int comparison;

            if (left.TryNumber(out var leftNumber) && right.TryNumber(out var rightNumber))
            {
                comparison = leftNumber.CompareTo(rightNumber);
            }
            else
            {
                comparison = string.Compare(left.ToText(), right.ToText(), StringComparison.OrdinalIgnoreCase);
            }

            left = Value.FromBoolean(op switch
            {
                "=" => comparison == 0,
                "<>" => comparison != 0,
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                ">" => comparison > 0,
                _ => comparison >= 0,
            });
        }
    }

    private string ReadComparisonOperator()
    {
        foreach (var candidate in new[] { "<>", "<=", ">=", "=", "<", ">" })
        {
            if (string.CompareOrdinal(_text, _position, candidate, 0, candidate.Length) == 0)
            {
                _position += candidate.Length;

                return candidate;
            }
        }

        return null;
    }

    private Value ParseConcatenation()
    {
        var left = ParseAdditive();

        while (true)
        {
            SkipWhitespace();

            if (!TryConsume('&'))
            {
                return left;
            }

            var right = ParseAdditive();

            left = left.IsError
                ? left
                : right.IsError
                    ? right
                    : Value.FromText(left.ToText() + right.ToText());
        }
    }

    private Value ParseAdditive()
    {
        var left = ParseMultiplicative();

        while (true)
        {
            SkipWhitespace();

            if (TryConsume('+'))
            {
                left = Arithmetic(left, ParseMultiplicative(), static (a, b) => a + b);
            }
            else if (TryConsume('-'))
            {
                left = Arithmetic(left, ParseMultiplicative(), static (a, b) => a - b);
            }
            else
            {
                return left;
            }
        }
    }

    private Value ParseMultiplicative()
    {
        var left = ParsePower();

        while (true)
        {
            SkipWhitespace();

            if (TryConsume('*'))
            {
                left = Arithmetic(left, ParsePower(), static (a, b) => a * b);
            }
            else if (TryConsume('/'))
            {
                var right = ParsePower();

                left = right.TryNumber(out var divisor) && divisor == 0 && !left.IsError
                    ? Value.Error("#DIV/0!")
                    : Arithmetic(left, right, static (a, b) => a / b);
            }
            else
            {
                return left;
            }
        }
    }

    private Value ParsePower()
    {
        var left = ParseUnary();

        SkipWhitespace();

        if (TryConsume('^'))
        {
            return Arithmetic(left, ParsePower(), Math.Pow);
        }

        return left;
    }

    private Value ParseUnary()
    {
        SkipWhitespace();

        if (TryConsume('-'))
        {
            return Arithmetic(Value.FromNumber(0), ParseUnary(), static (a, b) => a - b);
        }

        if (TryConsume('+'))
        {
            return ParseUnary();
        }

        var value = ParsePrimary();

        SkipWhitespace();

        if (TryConsume('%'))
        {
            return Arithmetic(value, Value.FromNumber(100), static (a, b) => a / b);
        }

        return value;
    }

    private Value ParsePrimary()
    {
        SkipWhitespace();

        if (_position >= _text.Length)
        {
            throw new FormatException();
        }

        var character = _text[_position];

        if (character == '(')
        {
            _position++;
            var inner = ParseComparison();
            Expect(')');

            return inner;
        }

        if (character == '"')
        {
            return Value.FromText(ReadString());
        }

        if (character == '{')
        {
            var close = _text.IndexOf('}', _position + 1);

            if (close < 0)
            {
                throw new FormatException();
            }

            var name = _text[(_position + 1)..close].Trim();
            _position = close + 1;

            if (string.Equals(name, "row", StringComparison.OrdinalIgnoreCase))
            {
                return Value.FromNumber(_rowNumber);
            }

            var cell = _resolveColumn(name);

            return cell is null
                ? Value.Error("#REF!")
                : Value.FromCell(cell);
        }

        if (char.IsAsciiDigit(character) || character == '.')
        {
            return Value.FromNumber(ReadNumber());
        }

        if (char.IsLetter(character))
        {
            var name = ReadIdentifier();

            SkipWhitespace();

            if (TryConsume('('))
            {
                return CallFunction(name.ToUpperInvariant(), ReadArguments());
            }

            return name.ToUpperInvariant() switch
            {
                "TRUE" => Value.FromBoolean(true),
                "FALSE" => Value.FromBoolean(false),
                _ => throw new FormatException(),
            };
        }

        throw new FormatException();
    }

    private List<Func<Value>> ReadArguments()
    {
        // Arguments are captured as deferred evaluations so IF and IFERROR only evaluate the branch they
        // take, the way a spreadsheet does; an error in the untaken branch must not leak into the result.
        var arguments = new List<Func<Value>>();

        SkipWhitespace();

        if (TryConsume(')'))
        {
            return arguments;
        }

        while (true)
        {
            var start = _position;
            SkipArgument();
            var end = _position;
            var source = _text[start..end];
            arguments.Add(() => new PdfFormulaEvaluator(source, _resolveColumn, _rowNumber).ParseStandalone());

            SkipWhitespace();

            if (TryConsume(','))
            {
                continue;
            }

            Expect(')');

            return arguments;
        }
    }

    private Value ParseStandalone()
    {
        var value = ParseComparison();
        SkipWhitespace();

        if (_position < _text.Length)
        {
            throw new FormatException();
        }

        return value;
    }

    private void SkipArgument()
    {
        var depth = 0;
        var quoted = false;

        while (_position < _text.Length)
        {
            var character = _text[_position];

            if (quoted)
            {
                if (character == '"')
                {
                    quoted = false;
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character is '(' or '{')
            {
                depth++;
            }
            else if (character is ')' or '}')
            {
                if (depth == 0)
                {
                    return;
                }

                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                return;
            }

            _position++;
        }
    }

    private static Value CallFunction(string name, List<Func<Value>> arguments)
    {
        switch (name)
        {
            case "IF":
                {
                    if (arguments.Count < 2)
                    {
                        return Value.Error("#VALUE!");
                    }

                    var condition = arguments[0]();

                    if (condition.IsError)
                    {
                        return condition;
                    }

                    return condition.IsTrue()
                        ? arguments[1]()
                        : arguments.Count > 2
                            ? arguments[2]()
                            : Value.FromBoolean(false);
                }

            case "IFERROR":
                {
                    if (arguments.Count < 2)
                    {
                        return Value.Error("#VALUE!");
                    }

                    var value = arguments[0]();

                    return value.IsError
                        ? arguments[1]()
                        : value;
                }

            case "NOT":
                {
                    var value = arguments.Count > 0 ? arguments[0]() : Value.Error("#VALUE!");

                    return value.IsError
                        ? value
                        : Value.FromBoolean(!value.IsTrue());
                }

            case "AND":
            case "OR":
                {
                    var values = arguments.Select(argument => argument()).ToList();
                    var error = values.FirstOrDefault(value => value.IsError);

                    if (error.IsError)
                    {
                        return error;
                    }

                    return Value.FromBoolean(name == "AND"
                        ? values.All(value => value.IsTrue())
                        : values.Any(value => value.IsTrue()));
                }

            case "ABS":
                return Unary(arguments, Math.Abs);

            case "ROUND":
            case "ROUNDUP":
            case "ROUNDDOWN":
                {
                    var values = Numbers(arguments, out var error);

                    if (error.IsError)
                    {
                        return error;
                    }

                    if (values.Count == 0)
                    {
                        return Value.Error("#VALUE!");
                    }

                    var digits = values.Count > 1 ? (int)values[1] : 0;
                    var factor = Math.Pow(10, digits);

                    return Value.FromNumber(name switch
                    {
                        "ROUNDUP" => Math.Sign(values[0]) * Math.Ceiling(Math.Abs(values[0]) * factor) / factor,
                        "ROUNDDOWN" => Math.Sign(values[0]) * Math.Floor(Math.Abs(values[0]) * factor) / factor,
                        _ => Math.Round(values[0], Math.Clamp(digits, 0, 15), MidpointRounding.AwayFromZero),
                    });
                }

            case "MIN":
            case "MAX":
            case "SUM":
            case "AVERAGE":
                {
                    var values = Numbers(arguments, out var error);

                    if (error.IsError)
                    {
                        return error;
                    }

                    if (values.Count == 0)
                    {
                        return name == "AVERAGE" ? Value.Error("#DIV/0!") : Value.FromNumber(0);
                    }

                    return Value.FromNumber(name switch
                    {
                        "MIN" => values.Min(),
                        "MAX" => values.Max(),
                        "SUM" => values.Sum(),
                        _ => values.Average(),
                    });
                }

            default:
                return Value.Error("#NAME?");
        }
    }

    private static Value Unary(List<Func<Value>> arguments, Func<double, double> operation)
    {
        var values = Numbers(arguments, out var error);

        if (error.IsError)
        {
            return error;
        }

        return values.Count == 0
            ? Value.Error("#VALUE!")
            : Value.FromNumber(operation(values[0]));
    }

    private static List<double> Numbers(List<Func<Value>> arguments, out Value error)
    {
        var numbers = new List<double>(arguments.Count);
        error = default;

        foreach (var argument in arguments)
        {
            var value = argument();

            if (value.IsError)
            {
                error = value;

                return numbers;
            }

            if (value.TryNumber(out var number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private static Value Arithmetic(Value left, Value right, Func<double, double, double> operation)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (!left.TryNumber(out var a) || !right.TryNumber(out var b))
        {
            return Value.Error("#VALUE!");
        }

        var result = operation(a, b);

        return double.IsNaN(result) || double.IsInfinity(result)
            ? Value.Error("#NUM!")
            : Value.FromNumber(result);
    }

    private double ReadNumber()
    {
        var start = _position;

        while (_position < _text.Length && (char.IsAsciiDigit(_text[_position]) || _text[_position] == '.'))
        {
            _position++;
        }

        if (_position < _text.Length && _text[_position] is 'e' or 'E')
        {
            _position++;

            if (_position < _text.Length && _text[_position] is '+' or '-')
            {
                _position++;
            }

            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        return double.Parse(_text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private string ReadString()
    {
        var builder = new System.Text.StringBuilder();
        _position++;

        while (_position < _text.Length)
        {
            var character = _text[_position++];

            if (character == '"')
            {
                // A doubled quote is a literal quote inside the string.
                if (_position < _text.Length && _text[_position] == '"')
                {
                    builder.Append('"');
                    _position++;

                    continue;
                }

                return builder.ToString();
            }

            builder.Append(character);
        }

        throw new FormatException();
    }

    private string ReadIdentifier()
    {
        var start = _position;

        while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] is '_' or '.'))
        {
            _position++;
        }

        return _text[start.._position];
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
        {
            _position++;
        }
    }

    private bool TryConsume(char character)
    {
        if (_position < _text.Length && _text[_position] == character)
        {
            _position++;

            return true;
        }

        return false;
    }

    private void Expect(char character)
    {
        SkipWhitespace();

        if (!TryConsume(character))
        {
            throw new FormatException();
        }
    }

    /// <summary>
    /// A formula value: a number, text, a boolean, or an error.
    /// </summary>
    private readonly record struct Value(double Number, string Text, bool? Boolean, string ErrorCode)
    {
        public bool IsError => ErrorCode is not null;

        public static Value FromNumber(double number)
        {
            return new Value(number, null, null, null);
        }

        public static Value FromText(string text)
        {
            return new Value(0, text ?? string.Empty, null, null);
        }

        public static Value FromBoolean(bool value)
        {
            return new Value(0, null, value, null);
        }

        public static Value Error(string code)
        {
            return new Value(0, null, null, code);
        }

        public static Value FromCell(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                // An empty cell is zero in arithmetic and empty in text, which is how a spreadsheet reads it.
                return FromNumber(0);
            }

            return PdfCellValues.TryParseNumber(cell, percentAsFraction: true, out var number)
                ? FromNumber(number)
                : FromText(cell);
        }

        public bool TryNumber(out double number)
        {
            if (Boolean.HasValue)
            {
                number = Boolean.Value ? 1 : 0;

                return true;
            }

            if (Text is null)
            {
                number = Number;

                return !IsError;
            }

            return PdfCellValues.TryParseNumber(Text, percentAsFraction: true, out number);
        }

        public bool IsTrue()
        {
            if (Boolean.HasValue)
            {
                return Boolean.Value;
            }

            if (Text is not null)
            {
                return bool.TryParse(Text, out var parsed) && parsed;
            }

            return Number != 0;
        }

        public string ToText()
        {
            if (IsError)
            {
                return ErrorCode;
            }

            if (Boolean.HasValue)
            {
                return Boolean.Value ? "TRUE" : "FALSE";
            }

            return Text ?? PdfCellValues.ToInvariant(Number);
        }
    }
}
