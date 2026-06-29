using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// System tool that turns model-generated content into a downloadable file (for example PDF, Word,
/// Markdown, HTML, or CSV) attached to the active conversation. The file is surfaced to the chat UI as
/// a download through the standard generated-document reference, mirroring how charts are rendered.
/// </summary>
public sealed class GenerateFileTool : AIFunction
{
    public const string TheName = "generate_file";

    private const string DefaultExtension = ".md";

    private static readonly HashSet<string> _tabularExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv",
        ".tsv",
        ".xlsx",
    };

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "content": {
          "type": "string",
          "description": "The content to place in the file. Use Markdown or plain text for document formats (pdf, docx, md, txt, html). For spreadsheet or CSV formats, provide the data as CSV text with a header row."
        },
        "file_name": {
          "type": "string",
          "description": "Optional file name to show to the user, including the desired extension (for example 'summary.pdf'). The extension determines the output format."
        },
        "format": {
          "type": "string",
          "description": "Optional output format/extension (for example 'pdf', 'docx', 'md', 'html', 'csv', 'xlsx'). Used only when 'file_name' has no extension."
        },
        "title": {
          "type": "string",
          "description": "Optional title or heading for the generated document."
        }
      },
      "required": ["content"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Creates a downloadable file (PDF, Word, Markdown, HTML, text, CSV, or spreadsheet) from generated content and attaches it to the conversation. Use this whenever the user asks to generate, produce, or download a file. Returns a [doc:N] marker that MUST be included exactly as-is in your response so the UI renders the download link.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invokes core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<GenerateFileTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("content", out var content) || string.IsNullOrWhiteSpace(content))
        {
            return "A 'content' value is required to generate a file.";
        }

        var resolver = arguments.Services.GetRequiredService<IGeneratedFileWriterResolver>();
        arguments.TryGetFirstString("file_name", out var fileName);
        arguments.TryGetFirstString("format", out var format);
        arguments.TryGetFirstString("title", out var title);

        var extension = ResolveExtension(fileName, format);

        if (!resolver.IsSupported(extension))
        {
            var supported = string.Join(", ", resolver.SupportedExtensions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            return $"The '{extension}' format is not supported. Supported formats are: {supported}.";
        }

        var scope = GeneratedDocumentScope.Resolve();

        if (scope is null)
        {
            return "A downloadable file can only be created from an active chat session or chat interaction.";
        }

        fileName = NormalizeFileName(fileName, title, extension);

        var fileContent = BuildContent(content, title, extension, fileName);

        try
        {
            var service = arguments.Services.GetRequiredService<IGeneratedDocumentService>();
            var result = await service.CreateAsync(
                new GeneratedDocumentRequest(scope.Value.ReferenceId, scope.Value.ReferenceType, fileName, fileContent),
                cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return string.IsNullOrEmpty(result.ReferenceToken)
                ? $"Created \"{result.Document.FileName}\". The generated document id is {result.Document.ItemId}."
                : $"Created \"{result.Document.FileName}\". Include the following marker exactly as-is in your response so the user can download it: {result.ReferenceToken}";
        }
        catch (NotSupportedException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating file for tool '{ToolName}'.", Name);

            return "An error occurred while generating the file.";
        }
    }

    private static GeneratedFileContent BuildContent(string content, string title, string extension, string fileName)
    {
        var fileContent = new GeneratedFileContent
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Text = content,
        };

        if (_tabularExtensions.Contains(extension))
        {
            var (header, rows) = DelimitedDataParser.Parse(content, fileName);

            if (header.Count > 0)
            {
                fileContent.Header = header;
                fileContent.Rows = rows;
            }
        }

        return fileContent;
    }

    private static string ResolveExtension(string fileName, string format)
    {
        var fromFileName = GeneratedFileWriterOptions.Normalize(Path.GetExtension(fileName ?? string.Empty));

        if (!string.IsNullOrEmpty(fromFileName))
        {
            return fromFileName;
        }

        var fromFormat = GeneratedFileWriterOptions.Normalize(format);

        return !string.IsNullOrEmpty(fromFormat)
            ? fromFormat
            : DefaultExtension;
    }

    private static string NormalizeFileName(string fileName, string title, string extension)
    {
        var baseName = !string.IsNullOrWhiteSpace(fileName)
            ? Path.GetFileNameWithoutExtension(fileName.Trim())
            : null;

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = !string.IsNullOrWhiteSpace(title)
                ? title.Trim()
                : "generated-file";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(baseName.Length);

        foreach (var character in baseName)
        {
            builder.Append(invalidCharacters.Contains(character)
                ? '_'
                : character);
        }

        baseName = builder.ToString().Trim();

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "generated-file";
        }

        if (baseName.Length > 124)
        {
            baseName = baseName[..124];
        }

        return baseName + extension;
    }
}
