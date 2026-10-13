using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Animations.Endpoints;
using CrestApps.Core.AI.Animations.Rendering;
using CrestApps.Core.AI.Animations.Services;
using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Animations.Tools;

/// <summary>
/// Stores a rendered video with its conversation and registers the <c>[fig:N]</c> marker the chat replaces with a
/// video player, numbered the same way as the picture previews.
/// </summary>
internal static class AnimationPublisher
{
    /// <summary>
    /// The reference type a video reference is registered under. It is neither a document nor a data source, so
    /// the chat never lists it as a download or a citation.
    /// </summary>
    public const string ReferenceType = "animation";

    /// <summary>
    /// Stores the video and registers its marker.
    /// </summary>
    /// <param name="services">The services the tool resolved before rendering.</param>
    /// <param name="invocation">The current invocation.</param>
    /// <param name="owner">The conversation the video belongs to.</param>
    /// <param name="output">The rendered video.</param>
    /// <param name="title">The video's title.</param>
    /// <param name="settingsJson">The settings it was rendered with: its length, size, seed and look.</param>
    /// <param name="script">The script it was drawn with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored animation's identifier and its marker; the marker is <see langword="null"/> when this host does not serve videos.</returns>
    public static async Task<(string AnimationId, string Marker)> PublishAsync(
        PublishingServices services,
        AIInvocationContext invocation,
        AnimationOwner owner,
        AnimationRenderOutput output,
        string title,
        string settingsJson,
        string script,
        CancellationToken cancellationToken)
    {
        var animationId = await services.Store.SaveAsync(
            owner,
            new AnimationContent
            {
                VideoFilePath = output.VideoPath,
                Poster = output.Poster,
                SettingsJson = settingsJson,
                Script = script,
            },
            cancellationToken);

        var videoLink = ResolveLink(services, owner, animationId, AnimationAsset.Video);

        if (videoLink is null)
        {
            return (animationId, null);
        }

        var index = FigureReferenceMarker.NextIndex(invocation);
        var marker = FigureReferenceMarker.Format(index);

        invocation.ToolReferences[marker] = new AICompletionReference
        {
            Text = title,
            Title = title,
            Link = videoLink,
            PosterLink = ResolveLink(services, owner, animationId, AnimationAsset.Poster),
            IsVideo = true,
            Index = index,
            ReferenceId = animationId,
            ReferenceType = ReferenceType,
        };

        // A spoken reply never contains the marker, so the host is asked to show the video itself.
        invocation.RequestFigureDisplay(marker);

        return (animationId, marker);
    }

    /// <summary>
    /// Writes the tool response that makes the model repeat the marker and describe the video.
    /// </summary>
    /// <param name="marker">The marker.</param>
    /// <param name="format">The video's frames.</param>
    /// <param name="durationSeconds">The video's length.</param>
    /// <param name="adjustments">What was changed to fit the limits.</param>
    public static string Instructions(string marker, AnimationFrameFormat format, double durationSeconds, IReadOnlyList<string> adjustments)
    {
        var builder = new StringBuilder();

        builder
            .AppendLine("WRITE THE FOLLOWING LINE IN YOUR ANSWER, EXACTLY AS SHOWN, ON A LINE OF ITS OWN:")
            .AppendLine()
            .AppendLine(marker)
            .AppendLine()
            .Append("That line is a placeholder the host replaces with the video player. The reader sees no video unless it appears in your answer character for character. Do NOT describe it, renumber it, or turn it into a link. Do not call ")
            .Append(RenderAnimationTool.TheName)
            .AppendLine(" again unless the user asks for a change.")
            .AppendLine()
            .AppendLine("Below the marker, tell the user in a sentence or two what the video shows and how long it is. Never answer with the marker alone.")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"The video is {durationSeconds:0.#} seconds long, {format.Width}×{format.Height} at {format.FrameRate} frames per second.")
            .AppendLine();

        if (adjustments.Count > 0)
        {
            builder.AppendLine().AppendLine("Changed to fit the limits; tell the user:");

            foreach (var adjustment in adjustments)
            {
                builder.Append("- ").AppendLine(adjustment);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string ResolveLink(PublishingServices services, AnimationOwner owner, string animationId, AnimationAsset asset)
    {
        if (services.LinkGenerator is null)
        {
            return null;
        }

        var values = new RouteValueDictionary
        {
            ["referenceType"] = owner.ReferenceType,
            ["referenceId"] = owner.ReferenceId,
            ["animationId"] = animationId,
            ["asset"] = asset == AnimationAsset.Poster ? "poster" : "video",
        };

        return services.HttpContext is null
            ? services.LinkGenerator.GetPathByName(AnimationAssetEndpoint.DefaultRouteName, values)
            : services.LinkGenerator.GetPathByName(services.HttpContext, AnimationAssetEndpoint.DefaultRouteName, values);
    }

    /// <summary>
    /// The services publishing needs, resolved when the tool starts.
    /// </summary>
    /// <remarks>
    /// A render can outlive the request that asked for it: some providers keep running a tool after the turn is
    /// cancelled, by which time the request's services are disposed. Resolving them up front lets the video still
    /// be stored rather than failing at the last step.
    /// </remarks>
    /// <param name="Store">The animation store.</param>
    /// <param name="LinkGenerator">The link generator, or <see langword="null"/> when the host has none.</param>
    /// <param name="HttpContext">The current request, or <see langword="null"/>.</param>
    public sealed record PublishingServices(IAnimationStore Store, LinkGenerator LinkGenerator, HttpContext HttpContext)
    {
        /// <summary>
        /// Resolves the services publishing needs from the tool's service provider.
        /// </summary>
        /// <param name="services">The service provider the tool was invoked with.</param>
        public static PublishingServices From(IServiceProvider services)
        {
            return new PublishingServices(
                services.GetRequiredService<IAnimationStore>(),
                services.GetService<LinkGenerator>(),
                services.GetService<IHttpContextAccessor>()?.HttpContext);
        }
    }
}
