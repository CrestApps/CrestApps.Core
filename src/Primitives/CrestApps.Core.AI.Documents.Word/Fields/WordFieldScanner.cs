using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Finds the fields of a document — both the simple form and the begin, code, separator, result, end form —
/// with their codes and the runs that hold their results.
/// </summary>
internal static class WordFieldScanner
{
    /// <summary>
    /// Finds every field under an element, in document order.
    /// </summary>
    /// <param name="root">The element: a body, a header, a footer or a paragraph.</param>
    /// <returns>The fields.</returns>
    public static List<WordField> Scan(OpenXmlElement root)
    {
        var fields = new List<WordField>();

        if (root is null)
        {
            return fields;
        }

        var open = new Stack<WordField>();

        foreach (var element in root.Descendants())
        {
            switch (element)
            {
                case SimpleField simple:
                    fields.Add(new WordField
                    {
                        Instruction = simple.Instruction?.Value?.Trim() ?? string.Empty,
                        SimpleField = simple,
                        ResultRuns = [.. simple.Elements<Run>()],
                        Paragraph = simple.Ancestors<Paragraph>().FirstOrDefault(),
                    });

                    break;

                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Begin:
                    open.Push(new WordField
                    {
                        BeginRun = character.Parent as Run,
                        Paragraph = character.Ancestors<Paragraph>().FirstOrDefault(),
                    });

                    break;

                case FieldCode code when open.Count > 0 && !open.Peek().Separated:
                    open.Peek().InstructionBuilder.Append(code.Text);

                    break;

                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Separate && open.Count > 0:
                    open.Peek().Separated = true;
                    open.Peek().SeparateRun = character.Parent as Run;

                    break;

                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.End && open.Count > 0:
                    var field = open.Pop();

                    field.EndRun = character.Parent as Run;
                    field.Instruction = field.InstructionBuilder.ToString().Trim();
                    fields.Add(field);

                    // A field nested in this one's code is part of the code, not a field of its own result.
                    if (open.Count > 0 && !open.Peek().Separated)
                    {
                        open.Peek().InstructionBuilder.Append(' ');
                    }

                    break;

                case Run run when open.Count > 0 && open.Peek().Separated && run.GetFirstChild<FieldChar>() is null:
                    open.Peek().ResultRuns.Add(run);

                    break;
            }
        }

        return fields;
    }

    /// <summary>
    /// Returns a field's type, the first word of its code, in upper case.
    /// </summary>
    /// <param name="instruction">The field code.</param>
    /// <returns>The type, such as <c>PAGE</c>, <c>TOC</c> or <c>REF</c>.</returns>
    public static string TypeOf(string instruction)
    {
        var trimmed = (instruction ?? string.Empty).TrimStart();
        var end = 0;

        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '\\')
        {
            end++;
        }

        return trimmed[..end].ToUpperInvariant();
    }

    /// <summary>
    /// Returns a field's first argument: the bookmark of a <c>REF</c>, the sequence name of a <c>SEQ</c>.
    /// </summary>
    /// <param name="instruction">The field code.</param>
    /// <returns>The argument, or an empty string.</returns>
    public static string ArgumentOf(string instruction)
    {
        var parts = (instruction ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || parts[1].StartsWith('\\'))
        {
            return string.Empty;
        }

        return parts[1].Trim('"');
    }

    /// <summary>
    /// Replaces the result a field shows, keeping the formatting of its first result run.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="result">The new result.</param>
    public static void SetResult(WordField field, string result)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.ResultRuns.Count > 0)
        {
            var first = field.ResultRuns[0];

            foreach (var text in first.Elements<Text>().ToList())
            {
                text.Remove();
            }

            first.Append(new Text(result ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });

            foreach (var extra in field.ResultRuns.Skip(1))
            {
                extra.Remove();
            }

            field.ResultRuns = [first];

            return;
        }

        if (field.SimpleField is not null)
        {
            var run = new Run(new Text(result ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });

            field.SimpleField.Append(run);
            field.ResultRuns = [run];

            return;
        }

        if (field.SeparateRun is not null)
        {
            var properties = field.SeparateRun.RunProperties?.CloneNode(true);
            var run = new Run(new Text(result ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });

            if (properties is not null)
            {
                run.PrependChild(properties);
            }

            field.SeparateRun.InsertAfterSelf(run);
            field.ResultRuns = [run];
        }
    }
}

/// <summary>
/// One field of a document.
/// </summary>
internal sealed class WordField
{
    /// <summary>
    /// Gets or sets the field code.
    /// </summary>
    public string Instruction { get; set; }

    /// <summary>
    /// Gets the field code as it is read.
    /// </summary>
    public StringBuilder InstructionBuilder { get; } = new();

    /// <summary>
    /// Gets or sets the simple field element, for a field in the simple form.
    /// </summary>
    public SimpleField SimpleField { get; set; }

    /// <summary>
    /// Gets or sets the run that begins the field.
    /// </summary>
    public Run BeginRun { get; set; }

    /// <summary>
    /// Gets or sets the run that separates the code from the result.
    /// </summary>
    public Run SeparateRun { get; set; }

    /// <summary>
    /// Gets or sets the run that ends the field.
    /// </summary>
    public Run EndRun { get; set; }

    /// <summary>
    /// Gets or sets the runs that hold the field's result.
    /// </summary>
    public List<Run> ResultRuns { get; set; } = [];

    /// <summary>
    /// Gets or sets the paragraph the field starts in.
    /// </summary>
    public Paragraph Paragraph { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the separator has been read.
    /// </summary>
    public bool Separated { get; set; }

    /// <summary>
    /// Gets the field's type, such as <c>PAGE</c> or <c>TOC</c>.
    /// </summary>
    public string Type => WordFieldScanner.TypeOf(Instruction);

    /// <summary>
    /// Gets the text the field currently shows.
    /// </summary>
    public string Result => string.Concat(ResultRuns.SelectMany(run => run.Elements<Text>()).Select(text => text.Text));
}
