using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Lists, inspects, creates, changes and deletes a document's paragraph and character styles.
/// </summary>
internal sealed class ManageWordStylesTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ManageWordStyles;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "action": { "type": "string", "enum": ["list", "get", "create", "update", "delete"] },
            "name": { "type": "string", "description": "The style's name, such as 'Heading 1' or 'Call Out'." },
            "type": { "type": "string", "enum": ["paragraph", "character"], "description": "For create. Default paragraph." },
            "based_on": { "type": "string", "description": "The style it inherits from. Default Normal." },
            "next_style": { "type": "string", "description": "The style of the paragraph that follows one in this style." },
            "new_name": { "type": "string", "description": "For update: rename the style." },
            "format": {{WordToolSchemas.RunFormat}},
            "paragraph_format": {{WordToolSchemas.ParagraphFormat}},
            "reassign_to": { "type": "string", "description": "For delete: the style its paragraphs move to. Default Normal." },
            "include_unused": { "type": "boolean", "description": "For list: also list styles nothing uses." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManageWordStylesTool"/> class.
    /// </summary>
    public ManageWordStylesTool()
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
    public override string Description => "Manages a Word document's styles: 'list' them with how many paragraphs use each, 'get' one style's look, 'create' a paragraph or character style (font, size, color, spacing, indents, based on another), 'update' a style (every paragraph in it follows), or 'delete' a custom style, moving its paragraphs to another. Apply a style with format_word_content apply_style.";

    /// <summary>
    /// Runs the style action.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "list").Trim().ToLowerInvariant();

        if (action is "list" or "get")
        {
            var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

            using var package = await context.OpenAsync(source, cancellationToken);

            return action == "list" ? List(package, arguments.GetBoolean("include_unused") == true) : Describe(package, Require(package, arguments.GetString("name")));
        }

        var name = arguments.GetString("name") ?? throw new WordToolException("Pass 'name'.");

        var (summary, document) = await context.EditAsync(arguments.Document(), $"Style {action}: {name}", edit =>
        {
            var package = edit.Package;
            var styles = WordStyleSheet.GetOrCreateStyles(package.MainPart);

            switch (action)
            {
                case "create":
                    if (Find(package, name) is not null)
                    {
                        throw new WordToolException($"The document already has a style named \"{name}\"; use action 'update'.");
                    }

                    var character = string.Equals(arguments.GetString("type"), "character", StringComparison.OrdinalIgnoreCase);
                    var style = new Style
                    {
                        Type = character ? StyleValues.Character : StyleValues.Paragraph,
                        StyleId = UniqueId(styles, name),
                        StyleName = new StyleName { Val = name.Trim() },
                        CustomStyle = true,
                        PrimaryStyle = new PrimaryStyle(),
                    };

                    if (!character)
                    {
                        style.BasedOn = new BasedOn { Val = arguments.GetString("based_on") is { } basedOn ? Require(package, basedOn).StyleId.Value : WordStyleSheet.Normal };
                    }

                    if (arguments.GetString("next_style") is { } next)
                    {
                        style.NextParagraphStyle = new NextParagraphStyle { Val = Require(package, next).StyleId.Value };
                    }

                    ApplyFormats(style, arguments);
                    styles.Append(style);

                    return Task.FromResult($"Created the {(character ? "character" : "paragraph")} style \"{name}\" (id {style.StyleId.Value}). Apply it with format_word_content apply_style.");

                case "update":
                    var existing = Require(package, name);

                    if (arguments.GetString("based_on") is { } newBase)
                    {
                        existing.BasedOn = new BasedOn { Val = Require(package, newBase).StyleId.Value };
                    }

                    if (arguments.GetString("next_style") is { } newNext)
                    {
                        existing.NextParagraphStyle = new NextParagraphStyle { Val = Require(package, newNext).StyleId.Value };
                    }

                    if (arguments.GetString("new_name") is { } newName)
                    {
                        existing.StyleName = new StyleName { Val = newName.Trim() };
                    }

                    ApplyFormats(existing, arguments);

                    return Task.FromResult($"Updated the style \"{existing.StyleName?.Val?.Value ?? name}\"; every paragraph in it follows.");

                case "delete":
                    var target = Require(package, name);

                    if (target.CustomStyle?.Value != true || target.Default?.Value == true)
                    {
                        throw new WordToolException($"\"{name}\" is a built-in style and cannot be deleted; change it with action 'update'.");
                    }

                    var replacement = arguments.GetString("reassign_to") is { } reassign ? Require(package, reassign).StyleId.Value : null;
                    var moved = 0;

                    foreach (var paragraph in package.MainPart.Document.Descendants<Paragraph>().Where(item => item.ParagraphProperties?.ParagraphStyleId?.Val?.Value == target.StyleId?.Value))
                    {
                        paragraph.ParagraphProperties.ParagraphStyleId = replacement is null ? null : new ParagraphStyleId { Val = replacement };
                        moved++;
                    }

                    foreach (var run in package.MainPart.Document.Descendants<RunProperties>().Where(item => item.RunStyle?.Val?.Value == target.StyleId?.Value))
                    {
                        run.RunStyle = null;
                        moved++;
                    }

                    target.Remove();

                    return Task.FromResult($"Deleted the style \"{name}\"; {moved} paragraph(s) or run(s) moved to {replacement ?? "the default style"}.");

                default:
                    throw new WordToolException("'action' must be list, get, create, update or delete.");
            }
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static void ApplyFormats(Style style, WordToolArguments arguments)
    {
        if (arguments.TryGetObject("format", out var formatElement) && WordFormatReader.ReadRun(formatElement) is { IsEmpty: false } format)
        {
            // The style's run properties hold the same elements as a run's, so the format is applied to a run's
            // and moved across.
            var properties = new RunProperties(style.StyleRunProperties?.ChildElements.Select(child => child.CloneNode(true)) ?? []);

            format.ApplyTo(properties);
            style.StyleRunProperties = new StyleRunProperties(properties.ChildElements.Where(child => child is not RunStyle).Select(child => child.CloneNode(true)));
        }

        if (arguments.TryGetObject("paragraph_format", out var paragraphElement) && WordFormatReader.ReadParagraph(paragraphElement) is { } paragraphFormat)
        {
            var properties = new ParagraphProperties(style.StyleParagraphProperties?.ChildElements.Select(child => child.CloneNode(true)) ?? []);

            paragraphFormat.ApplyTo(properties);
            style.StyleParagraphProperties = new StyleParagraphProperties(properties.ChildElements.Where(child => child is not ParagraphStyleId).Select(child => child.CloneNode(true)));
        }
    }

    private static string List(WordPackage package, bool includeUnused)
    {
        var styles = package.MainPart.StyleDefinitionsPart?.Styles?.Elements<Style>().ToList() ?? [];
        var index = new WordStyleIndex(package.MainPart);
        var paragraphUse = package.Body.Descendants<Paragraph>().GroupBy(index.StyleOf).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var runUse = package.Body.Descendants<RunStyle>().Where(item => item.Val?.Value is not null).GroupBy(item => item.Val.Value).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var answer = new StringBuilder();
        var listed = 0;

        foreach (var style in styles.Where(style => style.Type?.Value != StyleValues.Numbering).OrderBy(style => style.Type?.InnerText).ThenBy(style => style.StyleName?.Val?.Value))
        {
            var id = style.StyleId?.Value ?? string.Empty;
            var uses = paragraphUse.GetValueOrDefault(id) + runUse.GetValueOrDefault(id);

            if (uses == 0 && !includeUnused && style.CustomStyle?.Value != true && style.PrimaryStyle is null)
            {
                continue;
            }

            answer.Append("- \"").Append(style.StyleName?.Val?.Value ?? id).Append("\" (").Append(style.Type?.InnerText).Append(", id ").Append(id);

            if (style.BasedOn?.Val?.Value is { } basedOn)
            {
                answer.Append(", based on ").Append(index.NameOf(basedOn));
            }

            if (style.CustomStyle?.Value == true)
            {
                answer.Append(", custom");
            }

            answer.Append(CultureInfo.InvariantCulture, $"): used {uses} time(s)").AppendLine();
            listed++;
        }

        return $"{listed} style(s):\n" + answer.ToString().TrimEnd();
    }

    private static string Describe(WordPackage package, Style style)
    {
        var resolver = new WordStyleResolver(package.MainPart);
        var paragraph = new Paragraph(new ParagraphProperties { ParagraphStyleId = new ParagraphStyleId { Val = style.Type?.Value == StyleValues.Character ? null : style.StyleId?.Value } });
        var (format, run) = resolver.Resolve(paragraph);

        if (style.Type?.Value == StyleValues.Character)
        {
            run = resolver.ResolveRun(run, new RunProperties(new RunStyle { Val = style.StyleId?.Value }));
        }

        var chain = string.Join(" → ", resolver.Styles.Chain(style.StyleId?.Value).Select(item => item.StyleName?.Val?.Value ?? item.StyleId?.Value));

        return FormattableString.Invariant($"Style \"{style.StyleName?.Val?.Value}\" ({style.Type?.InnerText}, id {style.StyleId?.Value}; {chain}): {run.Font} {run.Size:0.#}pt, color #{run.Color}{(run.Bold ? ", bold" : string.Empty)}{(run.Italic ? ", italic" : string.Empty)}{(run.Underline ? ", underlined" : string.Empty)}; alignment {format.Alignment}, space before {format.SpaceBefore:0.#}pt, after {format.SpaceAfter:0.#}pt, line spacing {(format.LineRule == "auto" ? format.LineValue.ToString("0.##", CultureInfo.InvariantCulture) + "x" : format.LineValue.ToString("0.#", CultureInfo.InvariantCulture) + "pt " + format.LineRule)}, indent left {format.IndentLeft:0.#}pt, first line {format.FirstLine:0.#}pt{(format.KeepNext ? ", kept with next" : string.Empty)}.");
    }

    private static Style Find(WordPackage package, string name)
    {
        var id = WordStyleSheet.Find(package.MainPart, name, null);

        return id is null ? null : package.MainPart.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(style => style.StyleId?.Value == id);
    }

    private static Style Require(WordPackage package, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new WordToolException("Pass 'name'.");
        }

        return Find(package, name) ?? throw new WordToolException($"The document has no style \"{name}\". Call manage_word_styles with action 'list'.");
    }

    private static string UniqueId(Styles styles, string name)
    {
        var builder = new StringBuilder();

        foreach (var character in name)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        var baseId = builder.Length == 0 || !char.IsAsciiLetter(builder[0]) ? "Style" + builder : builder.ToString();
        var candidate = baseId;

        for (var number = 2; styles.Elements<Style>().Any(style => string.Equals(style.StyleId?.Value, candidate, StringComparison.OrdinalIgnoreCase)); number++)
        {
            candidate = baseId + number.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }
}
