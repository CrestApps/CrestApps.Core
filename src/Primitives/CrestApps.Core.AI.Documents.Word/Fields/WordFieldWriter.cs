using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Writes fields — page numbers, sequence numbers, references, a table of contents — as the begin, code,
/// separator, result and end runs Word itself writes, with a result already filled in so the field reads
/// correctly before Word updates it.
/// </summary>
internal static class WordFieldWriter
{
    /// <summary>
    /// Builds the runs of a field.
    /// </summary>
    /// <param name="instruction">The field code, such as <c>PAGE</c> or <c>SEQ Figure \* ARABIC</c>.</param>
    /// <param name="result">The result shown until the field is updated.</param>
    /// <param name="format">The formatting of every run, or <see langword="null"/>.</param>
    /// <param name="dirty">Whether Word should update the field when the document opens.</param>
    /// <returns>The runs, in order.</returns>
    public static List<Run> CreateRuns(string instruction, string result, WordRunFormat format = null, bool dirty = false)
    {
        var begin = new FieldChar { FieldCharType = FieldCharValues.Begin };

        if (dirty)
        {
            begin.Dirty = true;
        }

        return
        [
            WithFormat(new Run(begin), format),
            WithFormat(new Run(new FieldCode(" " + instruction.Trim() + " ") { Space = SpaceProcessingModeValues.Preserve }), format),
            WithFormat(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }), format),
            WordInlineWriter.CreateRun(result ?? string.Empty, format),
            WithFormat(new Run(new FieldChar { FieldCharType = FieldCharValues.End }), format),
        ];
    }

    /// <summary>
    /// Appends a field to a paragraph.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="instruction">The field code.</param>
    /// <param name="result">The result shown until the field is updated.</param>
    /// <param name="format">The formatting, or <see langword="null"/>.</param>
    public static void Append(OpenXmlCompositeElement paragraph, string instruction, string result, WordRunFormat format = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        foreach (var run in CreateRuns(instruction, result, format))
        {
            paragraph.Append(run);
        }
    }

    /// <summary>
    /// Asks Word to update every field when the document is opened, so page numbers, references and a table
    /// of contents are recomputed against Word's own pagination.
    /// </summary>
    /// <param name="package">The document.</param>
    public static void RequestUpdateOnOpen(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        WordSchemaOrder.Set(package.GetOrCreateSettings(), new UpdateFieldsOnOpen { Val = true });
    }

    private static Run WithFormat(Run run, WordRunFormat format)
    {
        if (format is { IsEmpty: false })
        {
            var properties = new RunProperties();

            format.ApplyTo(properties);
            run.PrependChild(properties);
        }

        return run;
    }
}
