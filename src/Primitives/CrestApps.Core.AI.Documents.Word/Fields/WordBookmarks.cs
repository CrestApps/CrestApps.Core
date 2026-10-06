using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Reads and writes bookmarks: the named places a hyperlink, a cross-reference or a table of contents entry
/// points at.
/// </summary>
internal sealed class WordBookmarks
{
    private readonly HashSet<string> _names;
    private int _nextId;

    private WordBookmarks(HashSet<string> names, int nextId)
    {
        _names = names;
        _nextId = nextId;
    }

    /// <summary>
    /// Reads the bookmarks a document has.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The bookmark registry.</returns>
    public static WordBookmarks For(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var starts = package.MainPart.Document.Descendants<BookmarkStart>().ToList();
        var names = starts.Select(start => start.Name?.Value).Where(name => !string.IsNullOrEmpty(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var largest = starts
            .Select(start => int.TryParse(start.Id?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max();

        return new WordBookmarks(names, largest + 1);
    }

    /// <summary>
    /// Returns whether a bookmark exists.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the document has the bookmark.</returns>
    public bool Contains(string name)
    {
        return !string.IsNullOrEmpty(name) && _names.Contains(name);
    }

    /// <summary>
    /// Returns a new bookmark id.
    /// </summary>
    /// <returns>The id.</returns>
    public int NextId()
    {
        return _nextId++;
    }

    /// <summary>
    /// Turns a name a model wrote into a valid bookmark name — a letter first, then letters, digits and
    /// underscores, at most 40 characters — and makes it unique.
    /// </summary>
    /// <param name="name">The name asked for.</param>
    /// <param name="fallbackPrefix">The prefix used when nothing of the name is usable.</param>
    /// <returns>The bookmark name.</returns>
    public string UniqueName(string name, string fallbackPrefix = "Bookmark")
    {
        var builder = new StringBuilder();

        foreach (var character in name ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character) || character == '-')
            {
                builder.Append('_');
            }
        }

        var cleaned = builder.ToString().Trim('_');

        if (cleaned.Length == 0 || !char.IsAsciiLetter(cleaned[0]))
        {
            cleaned = fallbackPrefix + (cleaned.Length == 0 ? string.Empty : "_" + cleaned);
        }

        if (cleaned.Length > 36)
        {
            cleaned = cleaned[..36];
        }

        var candidate = cleaned;

        for (var number = 2; _names.Contains(candidate); number++)
        {
            candidate = cleaned + "_" + number.ToString(CultureInfo.InvariantCulture);
        }

        _names.Add(candidate);

        return candidate;
    }

    /// <summary>
    /// Returns a new hidden bookmark name — an underscore prefix and nine digits, the way Word names the
    /// bookmarks a table of contents or a cross-reference points at, so they stay out of the bookmark list.
    /// </summary>
    /// <param name="prefix">The prefix, such as <c>_Toc</c> or <c>_Ref</c>.</param>
    /// <returns>The bookmark name.</returns>
    public string HiddenName(string prefix)
    {
        for (var number = 100_000_000 + _names.Count; ; number++)
        {
            var candidate = prefix + number.ToString(CultureInfo.InvariantCulture);

            if (_names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Wraps the content of a paragraph in a bookmark.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="name">The bookmark name, already unique.</param>
    public void Wrap(Paragraph paragraph, string name)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var id = NextId().ToString(CultureInfo.InvariantCulture);
        var start = new BookmarkStart { Name = name, Id = id };
        var properties = paragraph.ParagraphProperties;

        if (properties is null)
        {
            paragraph.PrependChild(start);
        }
        else
        {
            properties.InsertAfterSelf(start);
        }

        paragraph.Append(new BookmarkEnd { Id = id });
    }

    /// <summary>
    /// Finds the name of a bookmark that wraps a paragraph and starts with a prefix, such as the <c>_Toc</c>
    /// bookmark a table of contents links a heading by.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="prefix">The name prefix.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string FindOn(OpenXmlElement paragraph, string prefix)
    {
        return paragraph?.Elements<BookmarkStart>()
            .Select(start => start.Name?.Value)
            .FirstOrDefault(name => name is not null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
