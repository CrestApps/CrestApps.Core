using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Turns a deck into text for another use: an outline, the speaker notes, a presenter script, a handout or
/// plain text, shown in the answer or saved as a document.
/// </summary>
internal sealed class ExportPresentationContentTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ExportPresentationContent;

    private const int MaxInlineCharacters = 12_000;
    private const int WordsPerMinute = 130;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportPresentationContentTool"/> class.
    /// </summary>
    public ExportPresentationContentTool()
        : base(
            TheName,
            "Turns the presentation into text: 'outline' (slide titles and bullets as Markdown), 'notes' (the speaker notes), 'script' (what to say on each slide, from its notes or else its bullets, with timings), 'handout' (each slide's content with its notes, for the audience or a document), or 'text' (every piece of text). format 'inline' returns it to you to show or use; md, txt, docx or pdf saves it as a file for the user to download.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "kind": { "type": "string", "enum": ["outline", "notes", "script", "handout", "text"] },
                "format": { "type": "string", "enum": ["inline", "md", "txt", "docx", "pdf"], "description": "Defaults to inline." },
                "slides": { "type": ["integer", "string", "array"] },
                "file_name": { "type": "string" }
              },
              "required": ["kind"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Exports the content.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var requested = call.Slides(model, false);
        var slides = model.Slides.Where(slide => requested.Count == 0 ? !slide.Hidden : requested.Contains(slide.Number)).ToList();
        var kind = call.Arguments.String("kind", "content", "type")?.Trim().ToLowerInvariant() ?? "outline";
        var title = model.Title ?? deck.Name;

        var markdown = kind switch
        {
            "outline" => Outline(title, slides),
            "notes" or "speaker_notes" => Notes(title, slides),
            "script" or "speaker_script" => Script(title, slides),
            "handout" or "presenter" or "handouts" => Handout(title, slides),
            "text" or "all_text" => Text(model, slides),
            _ => throw new PresentationArgumentException("kind must be outline, notes, script, handout or text."),
        };

        var format = call.Arguments.String("format", "file_format", "extension")?.Trim().TrimStart('.').ToLowerInvariant() ?? "inline";

        if (format is "inline" or "text_response" or "chat")
        {
            return markdown.Length <= MaxInlineCharacters
                ? markdown
                : markdown[..MaxInlineCharacters] + $"\n\n(Truncated at {MaxInlineCharacters.ToString(CultureInfo.InvariantCulture)} characters; save it as a file with format md or docx for all of it.)";
        }

        var extension = "." + (format == "markdown" ? "md" : format);
        var writers = call.Services.GetService<IGeneratedFileWriterResolver>();
        var documents = call.Services.GetService<IGeneratedDocumentService>();

        if (documents is null || writers?.IsSupported(extension) != true)
        {
            throw new PresentationArgumentException($"This host cannot create {extension} files. Use format inline, md or txt.");
        }

        var name = call.Arguments.String("file_name", "filename") ?? $"{deck.Name} {kind}";
        var fileName = Path.GetFileNameWithoutExtension(string.Concat(name.Split(Path.GetInvalidFileNameChars()))).Trim() + extension;
        var result = await documents.CreateAsync(
            new GeneratedDocumentRequest(call.Session.ReferenceId, call.Session.ReferenceType, fileName, new GeneratedFileContent { Title = $"{title} — {kind}", Text = markdown }),
            cancellationToken);

        return string.IsNullOrEmpty(result.ReferenceToken)
            ? $"Created \"{result.Document.FileName}\". The generated document id is {result.Document.ItemId}."
            : $"Created the {kind} of \"{deck.Name}\". Return this download marker verbatim and do not call export_presentation_content again for this file: {result.ReferenceToken}";
    }

    private static string Outline(string title, List<PresentationSlide> slides)
    {
        var builder = new StringBuilder("# ").AppendLine(title).AppendLine();

        foreach (var slide in slides)
        {
            builder.Append("## ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(slide.Title ?? "(untitled)");

            foreach (var paragraph in BodyParagraphs(slide))
            {
                builder.Append(new string(' ', paragraph.Level * 2)).Append("- ").AppendLine(paragraph.Text.Trim());
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string Notes(string title, List<PresentationSlide> slides)
    {
        var builder = new StringBuilder("# ").Append(title).AppendLine(" — speaker notes").AppendLine();

        foreach (var slide in slides)
        {
            builder.Append("## Slide ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(": ").AppendLine(slide.Title ?? "(untitled)")
                .AppendLine(string.IsNullOrWhiteSpace(slide.Notes) ? "_No notes._" : slide.Notes.Trim())
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string Script(string title, List<PresentationSlide> slides)
    {
        var builder = new StringBuilder("# ").Append(title).AppendLine(" — presenter script").AppendLine();
        var elapsed = 0d;

        foreach (var slide in slides)
        {
            var spoken = string.IsNullOrWhiteSpace(slide.Notes)
                ? string.Join(" ", BodyParagraphs(slide).Select(paragraph => paragraph.Text.Trim().TrimEnd('.') + "."))
                : slide.Notes.Trim();

            var minutes = Math.Max(0.25, Words(spoken) / (double)WordsPerMinute);

            builder.Append("## Slide ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(slide.Title ?? "(untitled)")
                .Append(" (at ").Append(Clock(elapsed)).Append(", about ").Append(Clock(minutes)).AppendLine(")");
            builder.AppendLine(string.IsNullOrWhiteSpace(spoken) ? "_Nothing to say yet: the slide has no notes or body text._" : spoken);

            if (string.IsNullOrWhiteSpace(slide.Notes) && !string.IsNullOrWhiteSpace(spoken))
            {
                builder.AppendLine("_(From the slide's bullets; the slide has no speaker notes.)_");
            }

            builder.AppendLine();
            elapsed += minutes;
        }

        builder.Append("Total: about ").Append(Clock(elapsed)).Append(" at ").Append(WordsPerMinute.ToString(CultureInfo.InvariantCulture)).AppendLine(" words a minute.");

        return builder.ToString().TrimEnd();
    }

    private static string Handout(string title, List<PresentationSlide> slides)
    {
        var builder = new StringBuilder("# ").AppendLine(title).AppendLine();

        foreach (var slide in slides)
        {
            builder.Append("## ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(slide.Title ?? "(untitled)").AppendLine();

            foreach (var paragraph in BodyParagraphs(slide))
            {
                builder.Append(new string(' ', paragraph.Level * 2)).Append("- ").AppendLine(paragraph.Text.Trim());
            }

            foreach (var element in slide.AllElements())
            {
                if (element.Table is { } table)
                {
                    var rows = table.ToText();

                    if (rows.Count > 0)
                    {
                        builder.AppendLine().Append("| ").AppendJoin(" | ", rows[0].Select(Cell)).AppendLine(" |")
                            .Append('|').AppendJoin("|", rows[0].Select(_ => "---")).AppendLine("|");

                        foreach (var row in rows.Skip(1))
                        {
                            builder.Append("| ").AppendJoin(" | ", row.Select(Cell)).AppendLine(" |");
                        }
                    }
                }
                else if (element.Chart is { } chart)
                {
                    builder.AppendLine().Append("_Chart: ").Append(chart.Title ?? chart.Kind).Append(" — ")
                        .AppendJoin("; ", chart.Series.Select(series => (series.Name ?? "Series") + ": " + string.Join(", ", chart.Categories.Zip(series.Values, (category, value) => $"{category} {value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "–"}")))).AppendLine("_");
                }
                else if (element.Kind == PresentationElementKind.Picture && !string.IsNullOrWhiteSpace(element.AltText) && !element.IsDecorative)
                {
                    builder.AppendLine().Append("_Picture: ").Append(element.AltText.Trim()).AppendLine("_");
                }
            }

            if (!string.IsNullOrWhiteSpace(slide.Notes))
            {
                builder.AppendLine().Append("> ").AppendLine(slide.Notes.Trim().Replace("\n", "\n> ", StringComparison.Ordinal));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string Text(PresentationModel model, List<PresentationSlide> slides)
    {
        var numbers = slides.Select(slide => slide.Number).ToList();
        var builder = new StringBuilder();
        var current = 0;

        foreach (var entry in PresentationTextEntry.Collect(model, numbers, includeNotes: true))
        {
            if (entry.Slide != current)
            {
                current = entry.Slide;
                builder.AppendLine().Append("## Slide ").AppendLine(current.ToString(CultureInfo.InvariantCulture));
            }

            builder.AppendLine(entry.Where == "notes" ? "Notes: " + entry.Text : entry.Text);
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<PresentationParagraph> BodyParagraphs(PresentationSlide slide)
    {
        return slide.AllElements()
            .Where(element => !element.IsTitle && element.PlaceholderType is not ("dt" or "ftr" or "sldNum") && element.Text?.HasText == true)
            .SelectMany(element => element.Text.Paragraphs)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text));
    }

    private static string Cell(string text)
    {
        return (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
    }

    private static int Words(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string Clock(double minutes)
    {
        var span = TimeSpan.FromMinutes(minutes);

        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
