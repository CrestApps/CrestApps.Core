using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Stores slide pictures as documents the host serves and registers each under a <c>[fig:N]</c> marker the
/// model repeats in its answer, where the host draws the picture.
/// </summary>
/// <remarks>
/// The same mechanism the tabular preview uses, so the numbering of figures is shared: a slide preview and a
/// spreadsheet preview in one answer never claim the same marker.
/// </remarks>
internal static class PresentationFigurePublisher
{
    private const string PictureExtension = ".svg";

    /// <summary>
    /// Publishes pictures.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="session">The tool session, for the conversation the pictures belong to.</param>
    /// <param name="figures">The pictures: a caption, the SVG markup and a file name.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The markers in order, or <see langword="null"/> when this host cannot show pictures, in which case the
    /// caller answers in text.
    /// </returns>
    public static async Task<List<string>> PublishAsync(
        IServiceProvider services,
        PresentationToolSession session,
        IReadOnlyList<(string Caption, string Markup, string FileName)> figures,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var invocation = AIInvocationScope.Current;
        var documentService = services.GetService<IGeneratedDocumentService>();
        var writers = services.GetService<IGeneratedFileWriterResolver>();

        // Resolved rather than asked whether it is supported: the picture format is deliberately not one a
        // caller may request, so IsSupported reports false for it by design.
        if (invocation is null || documentService is null || writers?.TryResolve(PictureExtension, out _) != true || figures.Count == 0)
        {
            return null;
        }

        var index = FigureReferenceMarker.NextIndex(invocation);
        var pending = new List<(string Marker, AICompletionReference Reference)>(figures.Count);

        foreach (var (caption, markup, fileName) in figures)
        {
            var result = await documentService.CreateAsync(
                new GeneratedDocumentRequest(session.ReferenceId, session.ReferenceType, fileName, new GeneratedFileContent { Title = caption, Text = markup })
                {
                    // Shown where its marker sits, not listed under the answer as a download.
                    RegisterDownloadReference = false,
                },
                cancellationToken);

            var link = ResolveLink(services, result.Document.ItemId);

            if (string.IsNullOrEmpty(link))
            {
                if (logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    logger.LogDebug("Slide preview fell back to text: no '{RouteName}' endpoint is registered to serve the picture.", DownloadAIDocument.DefaultRouteName);
                }

                return null;
            }

            var marker = FigureReferenceMarker.Format(index);

            pending.Add((marker, new AICompletionReference
            {
                Text = caption,
                Title = caption,
                Link = link,
                IsImage = true,
                Index = index,
                ReferenceId = result.Document.ItemId,
                ReferenceType = AIReferenceTypes.DataSource.Document,
            }));

            if (logger?.IsEnabled(LogLevel.Debug) == true)
            {
                logger.LogDebug("Slide preview registered marker '{Marker}' for document '{DocumentId}' at '{Link}', {ByteCount} bytes of markup.", marker, result.Document.ItemId, link, markup.Length);
            }

            index++;
        }

        foreach (var (marker, reference) in pending)
        {
            invocation.ToolReferences[marker] = reference;

            // A spoken reply never contains the marker, so the host is asked to show the picture itself.
            invocation.RequestFigureDisplay(marker);
        }

        return pending.Select(entry => entry.Marker).ToList();
    }

    /// <summary>
    /// Writes the instructions that make the model repeat the markers.
    /// </summary>
    /// <param name="markers">The markers.</param>
    /// <param name="captions">What each picture shows, in order.</param>
    /// <param name="toolName">The name of the tool, for the "do not call again" hint.</param>
    /// <returns>The tool response text.</returns>
    public static string Instructions(IReadOnlyList<string> markers, IReadOnlyList<string> captions, string toolName)
    {
        var builder = new StringBuilder();

        builder
            .AppendLine("WRITE THE FOLLOWING LINE IN YOUR ANSWER, EXACTLY AS SHOWN, ON A LINE OF ITS OWN:")
            .AppendLine()
            .AppendLine(string.Join(' ', markers))
            .AppendLine()
            .Append("That line is a placeholder the host replaces with the slide pictures. The reader sees no pictures unless it appears in your answer character for character. Do NOT describe it, renumber it, turn it into a link or a list, or write that the pictures are \"shown above\" in place of it. Do not call ")
            .Append(toolName)
            .AppendLine(" again for slides that have not changed.")
            .AppendLine()
            .AppendLine("What each picture shows, in order, for your own wording only:");

        for (var position = 0; position < captions.Count; position++)
        {
            builder.Append((position + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(captions[position]);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Creates a file name for a picture that says what it is of.
    /// </summary>
    /// <param name="deckName">The deck's name.</param>
    /// <param name="suffix">What the picture shows, such as <c>slide-3</c>.</param>
    /// <returns>The file name.</returns>
    public static string FileName(string deckName, string suffix)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();

        foreach (var character in deckName ?? "presentation")
        {
            builder.Append(invalid.Contains(character) || character == ' ' ? '_' : character);
        }

        var baseName = builder.ToString().Trim('_');

        if (baseName.Length == 0)
        {
            baseName = "presentation";
        }

        if (baseName.Length > 80)
        {
            baseName = baseName[..80];
        }

        return baseName + "_" + suffix + PictureExtension;
    }

    /// <summary>
    /// Builds the address the host serves a generated document from, when it registered the endpoint.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="documentId">The document's identifier.</param>
    /// <returns>The link, or <see langword="null"/>.</returns>
    public static string ResolveLink(IServiceProvider services, string documentId)
    {
        var linkGenerator = services.GetService<LinkGenerator>();

        if (linkGenerator is null)
        {
            return null;
        }

        var values = new RouteValueDictionary
        {
            ["documentId"] = documentId,
        };

        try
        {
            var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;

            return httpContext is null
                ? linkGenerator.GetPathByName(DownloadAIDocument.DefaultRouteName, values)
                : linkGenerator.GetPathByName(httpContext, DownloadAIDocument.DefaultRouteName, values);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
