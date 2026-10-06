using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Fills in the results of reference fields: the text a <c>REF</c> field repeats from its bookmark, and — once
/// the document is laid out — the page numbers of <c>PAGEREF</c> fields and of the table of contents.
/// </summary>
internal static class WordReferenceUpdater
{
    /// <summary>
    /// Updates the reference fields of a document.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="services">The request services, or <see langword="null"/>.</param>
    public static void Update(WordPackage package, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(package);

        var fields = WordFieldScanner.Scan(package.Body);

        if (!fields.Any(field => field.Type is "REF" or "PAGEREF" or "TOC" or "INDEX"))
        {
            return;
        }

        var texts = ReadBookmarkTexts(package.Body);
        var order = package.Body.Descendants().Select((element, index) => (element, index)).ToDictionary(pair => pair.element, pair => pair.index, ReferenceEqualityComparer.Instance);
        var starts = package.Body.Descendants<BookmarkStart>().Where(start => start.Name?.Value is not null).GroupBy(start => start.Name.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields.Where(field => field.Type == "REF"))
        {
            var name = WordFieldScanner.ArgumentOf(field.Instruction);

            // With \p a reference says where its target is rather than repeating it.
            if (field.Instruction.Contains("\\p", StringComparison.OrdinalIgnoreCase) && starts.TryGetValue(name, out var start) && field.BeginRun is not null)
            {
                WordFieldScanner.SetResult(field, order[start] < order[field.BeginRun] ? "above" : "below");

                continue;
            }

            if (texts.TryGetValue(name, out var text))
            {
                WordFieldScanner.SetResult(field, text);
            }
        }

        WordTableOfContents.RefreshAll(package, services);
    }

    /// <summary>
    /// Reads the text every bookmark wraps.
    /// </summary>
    /// <param name="root">The body.</param>
    /// <returns>The text by bookmark name.</returns>
    public static Dictionary<string, string> ReadBookmarkTexts(OpenXmlElement root)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var open = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Each open field is true while its result is being read and false while its code is.
        var fields = new Stack<bool>();

        foreach (var element in root.Descendants())
        {
            switch (element)
            {
                case BookmarkStart start when start.Id?.Value is { } id && start.Name?.Value is { } name:
                    names[id] = name;
                    open[id] = new StringBuilder();

                    break;

                case BookmarkEnd end when end.Id?.Value is { } id && open.Remove(id, out var builder) && names.TryGetValue(id, out var name):
                    texts[name] = builder.ToString().Trim();

                    break;

                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Begin:
                    fields.Push(false);

                    break;

                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.Separate && fields.Count > 0:
                    fields.Pop();
                    fields.Push(true);

                    break;

                case FieldChar character when character.FieldCharType?.Value == FieldCharValues.End && fields.Count > 0:
                    fields.Pop();

                    break;

                case Text text when fields.Count == 0 || fields.Peek():
                    foreach (var collecting in open.Values)
                    {
                        collecting.Append(text.Text);
                    }

                    break;
            }
        }

        return texts;
    }
}
