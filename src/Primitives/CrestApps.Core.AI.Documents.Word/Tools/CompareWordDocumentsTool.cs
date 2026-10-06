using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Compares two Word documents element by element: what was added, removed and changed.
/// </summary>
internal sealed class CompareWordDocumentsTool : WordToolBase
{
    private const int MaxBlocks = 2000;

    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.CompareWordDocuments;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "original": { "type": "string", "description": "The earlier document: a working document's name or an uploaded file's name." },
            "revised": { "type": "string", "description": "The later document." },
            "limit": { "type": "integer", "description": "Most differences to list. Default 100." }
          },
          "required": ["original", "revised"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompareWordDocumentsTool"/> class.
    /// </summary>
    public CompareWordDocumentsTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Compares two Word documents (two uploads, an upload and its edited working copy, or two working documents) paragraph by paragraph and table by table, and lists what was added, removed and changed, with the nearest heading and the changed words. Use it to review versions or to summarize what an edit did.";

    /// <summary>
    /// Compares the documents.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var originalSource = await context.FindDocumentAsync(arguments.GetString("original") ?? throw new WordToolException("Pass 'original'."), cancellationToken);
        var revisedSource = await context.FindDocumentAsync(arguments.GetString("revised") ?? throw new WordToolException("Pass 'revised'."), cancellationToken);
        var limit = Math.Clamp(arguments.GetInt("limit") ?? 100, 1, 1000);

        var original = await ReadAsync(context, originalSource, cancellationToken);
        var revised = await ReadAsync(context, revisedSource, cancellationToken);
        var answer = new StringBuilder();
        var counts = new int[3];
        var listed = 0;
        string heading = null;

        foreach (var (kind, before, after) in Diff(original, revised))
        {
            var block = after ?? before;

            if (block.IsHeading)
            {
                heading = block.Text;
            }

            if (kind == ' ')
            {
                continue;
            }

            counts[kind switch { '+' => 0, '-' => 1, _ => 2 }]++;

            if (listed++ >= limit)
            {
                continue;
            }

            var under = heading is null || block.IsHeading ? string.Empty : $" under \"{WordText.Clip(heading, 50)}\"";

            switch (kind)
            {
                case '+':
                    answer.Append("- Added ").Append(Label(after)).Append(" [").Append(after.Id).Append(']').Append(under).Append(": \"").Append(WordText.Clip(TextOf(after), 200)).AppendLine("\"");

                    break;

                case '-':
                    answer.Append("- Removed ").Append(Label(before)).Append(under).Append(": \"").Append(WordText.Clip(TextOf(before), 200)).AppendLine("\"");

                    break;

                default:
                    answer.Append("- Changed ").Append(Label(after)).Append(" [").Append(after.Id).Append(']').Append(under).Append(": ").AppendLine(WordsChanged(TextOf(before), TextOf(after)));

                    break;
            }
        }

        if (counts.Sum() == 0)
        {
            return $"{originalSource.Describe()} and {revisedSource.Describe()} have the same text ({original.Count} elements). Formatting differences are not compared.";
        }

        var summary = string.Create(CultureInfo.InvariantCulture, $"Comparing {originalSource.Describe()} (original, {original.Count} elements) with {revisedSource.Describe()} (revised, {revised.Count} elements): {counts[0]} added, {counts[1]} removed, {counts[2]} changed.");

        return summary + "\n" + answer.ToString().TrimEnd() + (listed > limit ? $"\n…and {listed - limit} more." : string.Empty);
    }

    private static async Task<List<WordBlock>> ReadAsync(WordToolContext context, WordSource source, CancellationToken cancellationToken)
    {
        using var package = await context.OpenAsync(source, cancellationToken);

        var blocks = WordBlockReader.Read(package).Where(block => block.Kind is not (WordBlockKind.Empty or WordBlockKind.PageBreak or WordBlockKind.TableOfContents)).ToList();

        if (blocks.Count > MaxBlocks)
        {
            throw new WordToolException($"{source.Describe()} has {blocks.Count} elements; documents up to {MaxBlocks} elements can be compared.");
        }

        // The blocks are read once; the package can close, so each keeps its text, not its element.
        foreach (var block in blocks.Where(block => block.Kind == WordBlockKind.Table))
        {
            block.Text = WordText.Of(block.Element);
        }

        return blocks;
    }

    // A longest-common-subsequence alignment of the two element lists; a removal next to an addition of the same
    // kind of element is reported as a change.
    private static List<(char Kind, WordBlock Before, WordBlock After)> Diff(List<WordBlock> original, List<WordBlock> revised)
    {
        var lengths = new int[original.Count + 1, revised.Count + 1];

        for (var i = original.Count - 1; i >= 0; i--)
        {
            for (var j = revised.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = Same(original[i], revised[j]) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var steps = new List<(char Kind, WordBlock Before, WordBlock After)>();
        int x = 0, y = 0;

        while (x < original.Count || y < revised.Count)
        {
            if (x < original.Count && y < revised.Count && Same(original[x], revised[y]))
            {
                steps.Add((' ', original[x++], revised[y++]));
            }
            else if (y < revised.Count && (x == original.Count || lengths[x, y + 1] >= lengths[x + 1, y]))
            {
                steps.Add(('+', null, revised[y++]));
            }
            else
            {
                steps.Add(('-', original[x++], null));
            }
        }

        for (var index = 0; index < steps.Count - 1; index++)
        {
            if (steps[index].Kind == '-' && steps[index + 1].Kind == '+' && steps[index].Before.Kind == steps[index + 1].After.Kind)
            {
                steps[index] = ('~', steps[index].Before, steps[index + 1].After);
                steps.RemoveAt(index + 1);
            }
            else if (steps[index].Kind == '+' && steps[index + 1].Kind == '-' && steps[index].After.Kind == steps[index + 1].Before.Kind)
            {
                steps[index] = ('~', steps[index + 1].Before, steps[index].After);
                steps.RemoveAt(index + 1);
            }
        }

        return steps;
    }

    private static bool Same(WordBlock left, WordBlock right)
    {
        return left.Kind == right.Kind && string.Equals(Normalize(TextOf(left)), Normalize(TextOf(right)), StringComparison.Ordinal);
    }

    private static string TextOf(WordBlock block)
    {
        return block.Drawings.Count > 0 && string.IsNullOrWhiteSpace(block.Text) ? string.Join(", ", block.Drawings.Select(drawing => drawing.AltText ?? drawing.Name)) : block.Text ?? string.Empty;
    }

    private static string Normalize(string text)
    {
        return string.Join(' ', text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Label(WordBlock block)
    {
        return block.Kind switch
        {
            WordBlockKind.Heading => $"heading {block.Level}",
            WordBlockKind.BulletItem or WordBlockKind.NumberedItem => "list item",
            WordBlockKind.Table => "table",
            WordBlockKind.Image => "picture",
            _ => block.Kind.ToString().ToLowerInvariant(),
        };
    }

    // The words that differ, after the shared beginning and end are set aside.
    private static string WordsChanged(string before, string after)
    {
        var old = Normalize(before).Split(' ');
        var updated = Normalize(after).Split(' ');
        var prefix = 0;

        while (prefix < old.Length && prefix < updated.Length && old[prefix] == updated[prefix])
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < old.Length - prefix && suffix < updated.Length - prefix && old[^(suffix + 1)] == updated[^(suffix + 1)])
        {
            suffix++;
        }

        var removed = string.Join(' ', old[prefix..(old.Length - suffix)]);
        var added = string.Join(' ', updated[prefix..(updated.Length - suffix)]);

        return $"\"{WordText.Clip(removed, 150)}\" → \"{WordText.Clip(added, 150)}\"";
    }
}
