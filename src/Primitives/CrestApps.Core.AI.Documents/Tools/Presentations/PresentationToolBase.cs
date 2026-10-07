using System.Globalization;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Presentations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// What every presentation tool shares: its schema, its logging, the workspace session it opens, and the way
/// a problem is reported back to the model.
/// </summary>
/// <remarks>
/// A tool that cannot do what it was asked says so in words the model can act on — which slide does not
/// exist, which element names there are, what a length may look like — and makes no change at all. Edits are
/// applied as one batch by the engine, so a request is never left half done.
/// </remarks>
internal abstract class PresentationToolBase : AIFunction
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _schema;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationToolBase"/> class.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="description">What the tool does, for the model.</param>
    /// <param name="schema">The JSON schema of the tool's arguments.</param>
    protected PresentationToolBase(string name, string description, string schema)
    {
        _name = name;
        _description = description;
        _schema = JsonSerializer.Deserialize<JsonElement>(schema);
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => _description;

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _schema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Gets a value indicating whether the tool needs a deck to exist before it can run.
    /// </summary>
    protected virtual bool RequiresDeck => true;

    /// <summary>
    /// Invokes core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected sealed override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetService<ILoggerFactory>()?.CreateLogger(GetType().FullName);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        PresentationToolSession session = null;

        try
        {
            var reader = PresentationArguments.From(arguments);
            var (opened, error) = await PresentationToolSession.OpenAsync(arguments.Services, cancellationToken);

            if (error is not null)
            {
                return error;
            }

            session = opened;

            if (RequiresDeck && session.Workspace.State.Decks.Count == 0)
            {
                return "There is no presentation in this conversation yet. Start one with create_presentation, or ask the user to upload a .pptx file.";
            }

            var response = await ExecuteAsync(new PresentationToolCall(arguments.Services, reader, session, logger), cancellationToken);

            if (session.Imported.Count > 0 || session.ImportProblems.Count > 0)
            {
                response = ImportNote(session) + response;
            }

            if (logger?.IsEnabled(LogLevel.Debug) == true)
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return response;
        }
        catch (PresentationArgumentException exception)
        {
            return "Nothing was changed. " + exception.Message;
        }
        catch (PresentationEditException exception)
        {
            if (logger?.IsEnabled(LogLevel.Debug) == true)
            {
                logger.LogDebug(exception, "AI tool '{ToolName}' could not apply its change.", Name);
            }

            return "Nothing was changed. " + exception.Message;
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Runs the tool.
    /// </summary>
    /// <param name="call">The call: its arguments, the session and the services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response for the model.</returns>
    protected abstract Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the standard response to a change.
    /// </summary>
    /// <param name="deck">The deck that changed.</param>
    /// <param name="result">What the engine did.</param>
    /// <param name="notes">Anything else the model should know, such as where data came from.</param>
    /// <returns>The response.</returns>
    protected static string Changed(PresentationDeckState deck, PresentationEditResult result, IEnumerable<string> notes = null)
    {
        var builder = new StringBuilder();

        builder.Append("Done. \"").Append(deck.Name).Append("\" now has ").Append(PresentationDescriber.Count(result.SlideCount, "slide")).AppendLine(".");

        foreach (var change in result.Changes)
        {
            builder.Append("- ").AppendLine(change);
        }

        foreach (var note in notes ?? [])
        {
            builder.Append("- ").AppendLine(note);
        }

        if (result.CreatedElements.Count > 0)
        {
            builder.Append("New elements: ").AppendJoin("; ", result.CreatedElements.Select(created =>
                $"slide {created.SlideNumber.ToString(CultureInfo.InvariantCulture)} #{created.ElementId.ToString(CultureInfo.InvariantCulture)} \"{created.Name}\"")).AppendLine(".");
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine("Worth fixing:");

            foreach (var warning in result.Warnings.Distinct())
            {
                builder.Append("- ").AppendLine(warning);
            }
        }

        if (result.ChangedSlideNumbers.Count > 0)
        {
            builder.Append("To show the user, call preview_presentation with slides \"").Append(PresentationDescriber.Range(result.ChangedSlideNumbers)).AppendLine("\".");
        }

        return builder.ToString().TrimEnd();
    }

    private static string ImportNote(PresentationToolSession session)
    {
        var builder = new StringBuilder();

        foreach (var name in session.Imported)
        {
            builder.Append("(Loaded the uploaded deck \"").Append(name).AppendLine("\" into the workspace; the upload itself is never changed.)");
        }

        foreach (var problem in session.ImportProblems)
        {
            builder.Append('(').Append(problem).AppendLine(")");
        }

        return builder.ToString();
    }
}
