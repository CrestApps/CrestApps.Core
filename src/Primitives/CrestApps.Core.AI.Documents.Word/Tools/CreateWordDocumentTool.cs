using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Starts a new Word document: page setup, properties, theme and default styles, and optionally its first
/// content — or a copy of an uploaded template that keeps the template's styles, headers and footers.
/// </summary>
internal sealed class CreateWordDocumentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.CreateWordDocument;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The working document's name, used to refer to it later and as the download's file name." },
            "title": { "type": "string", "description": "The document title: written at the top in the Title style and stored as the title property." },
            "subtitle": { "type": "string" },
            "template": { "type": "string", "description": "An uploaded .docx or .dotx to start from. The new document keeps its styles, page setup, headers and footers; its body is emptied unless keep_template_content is true." },
            "keep_template_content": { "type": "boolean" },
            {{WordToolSchemas.Theme}},
            {{WordToolSchemas.PageSetup}},
            {{WordToolSchemas.Properties}},
            {{WordToolSchemas.Blocks}}
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateWordDocumentTool"/> class.
    /// </summary>
    public CreateWordDocumentTool()
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
    public override string Description => "Starts a new Word document in the workspace with its page setup, theme (fonts, colors, spacing), document properties and real styles (Title, Heading 1-6, Quote, Caption, lists), and optionally its first content blocks. Use 'template' to start from an uploaded .docx/.dotx so the document inherits the company's styles, headers and footers. Put as much content as you have in 'content' now; add more with add_word_content. Returns the document's name and the ids of its elements.";

    /// <summary>
    /// Creates the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.GetString("name") ?? arguments.GetString("title") ?? "document";
        var templateName = arguments.GetString("template");
        var template = templateName is null
            ? null
            : context.FindUpload(templateName, wordOnly: true)
                ?? throw new WordToolException($"There is no uploaded Word file named \"{templateName}\" to use as a template. {context.DescribeAvailable(await context.GetStateAsync(cancellationToken))}");

        var design = arguments.TryGetObject("theme", out var theme)
            ? WordSetupReader.ReadTheme(theme, null)
            : new WordDesign();

        return await context.MutateAsync(async state =>
        {
            using var package = template is null
                ? WordPackage.Create(design)
                : await OpenTemplateAsync(context, template, arguments.GetBoolean("keep_template_content") == true, cancellationToken);

            var edit = new WordEditContext(package, source: null, context.Options.Author, context.TimeProvider.GetUtcNow().UtcDateTime);

            if (template is null || arguments.TryGetObject("theme", out _))
            {
                edit.Design = template is null ? design : WordSetupReader.ReadTheme(theme, WordDesignReader.Infer(package.MainPart));

                if (template is not null)
                {
                    WordStyleSheet.Apply(package.MainPart, edit.Design);
                }
            }

            if (arguments.TryGetObject("page_setup", out var pageSetup))
            {
                foreach (var section in WordSections.All(package))
                {
                    WordSetupReader.ApplyPageSetup(section, pageSetup);
                }
            }

            var title = arguments.GetString("title");
            var properties = arguments.TryGetObject("properties", out var propertiesElement) ? propertiesElement : default;
            var setProperties = WordProperties.Apply(package.Document, properties, edit.Now);

            if (title is not null && !setProperties.Contains("title"))
            {
                package.Document.PackageProperties.Title = title;
            }

            package.Document.PackageProperties.Creator ??= context.Options.Author;
            package.Document.PackageProperties.Created ??= edit.Now;

            var builder = new WordContentBuilder(edit, context);
            var elements = new List<DocumentFormat.OpenXml.OpenXmlElement>();

            if (title is not null)
            {
                elements.Add(builder.Writer.Paragraph(title, builder.Writer.Style(WordStyleSheet.Title)));
            }

            if (arguments.GetString("subtitle") is { } subtitle)
            {
                elements.Add(builder.Writer.Paragraph(subtitle, builder.Writer.Style(WordStyleSheet.Subtitle)));
            }

            if (arguments.TryGetObject("content", out var content))
            {
                elements.AddRange(await builder.BuildAsync(content, cancellationToken));
            }

            WordBlockLocator.Insert(package, elements, after: null, before: null, at: "end");
            Fields.WordCaptions.Renumber(package);

            var blocks = WordBlockReader.Read(package);
            var section0 = WordSections.All(package)[0];
            var bytes = package.Save();
            var document = await context.AddDocumentAsync(
                state,
                name,
                bytes,
                edit.Design,
                template is null ? "Created" : $"Created from template \"{template.FileName}\"",
                cancellationToken: cancellationToken);

            var answer = new StringBuilder();

            answer.Append("Created working document \"").Append(document.Name).Append('"');

            if (template is not null)
            {
                answer.Append(" from template \"").Append(template.FileName).Append('"');
            }

            answer.Append(". Page: ").Append(WordDescriber.DescribeSection(section0)).AppendLine(".");
            answer.Append("Theme: ").Append(edit.Design.Preset).Append(" (").Append(edit.Design.BodyFont).Append(" body, ").Append(edit.Design.HeadingFont).AppendLine(" headings).");

            if (blocks.Count > 0)
            {
                answer.AppendLine().AppendLine("Elements:");

                foreach (var block in blocks.Take(150))
                {
                    answer.AppendLine(WordDescriber.Line(block, 100));
                }

                if (blocks.Count > 150)
                {
                    answer.Append("… and ").Append(blocks.Count - 150).AppendLine(" more; see get_word_document.");
                }
            }

            foreach (var warning in builder.Warnings)
            {
                answer.AppendLine().Append("Warning: ").Append(warning);
            }

            answer.AppendLine().AppendLine().Append("Next: add content with add_word_content, then preview_word to show it, then export_word for the .docx.");

            return answer.ToString();
        }, cancellationToken);
    }

    private static async Task<WordPackage> OpenTemplateAsync(WordToolContext context, CrestApps.Core.AI.Models.AIDocument template, bool keepContent, CancellationToken cancellationToken)
    {
        var bytes = await context.ReadUploadAsync(template, cancellationToken)
            ?? throw new WordToolException($"The stored file for \"{template.FileName}\" is missing. Ask the user to upload it again.");

        WordPackage package;

        try
        {
            package = WordPackage.Open(bytes);
        }
        catch (InvalidDataException ex)
        {
            throw new WordToolException($"\"{template.FileName}\" cannot be opened as a Word document. {ex.Message}", ex);
        }

        if (!keepContent)
        {
            // The template's look is kept — styles, the last section's page setup, headers and footers — and its
            // sample text is not.
            var body = package.Body;
            var section = WordSections.EnsureBodySection(body);

            foreach (var element in body.ChildElements.Where(element => !ReferenceEquals(element, section)).ToList())
            {
                element.Remove();
            }
        }

        return package;
    }
}
