namespace CrestApps.Core.AI.Models;

/// <summary>
/// The answer to "how is this conversation carried" for one chat surface: the chat mode the surface should
/// render, and — when the conversation is a speech-to-speech session — the deployment that carries it.
/// </summary>
/// <remarks>
/// <para>
/// Every chat surface used to work this out for itself, and every one of them squashed the mode to
/// <see cref="ChatMode.TextInput"/> when the chat deployment happened to be a realtime model, which is what
/// made a realtime profile voice-only. One resolution shared by the surfaces and both hubs is what lets a
/// typed message and a spoken one land in the same thread.
/// </para>
/// <para>
/// Plain settable properties rather than <c>init</c> accessors: this is constructed from runtime-compiled
/// Razor views in the MVC host, which compiles at a lower language version than the build does.
/// </para>
/// </remarks>
public sealed class ConversationModeResolution
{
    /// <summary>
    /// Gets or sets the chat mode the surface should render, after taking the deployments this installation
    /// actually has into account.
    /// </summary>
    public ChatMode ChatMode { get; set; }

    /// <summary>
    /// Gets or sets whether the conversation is carried by a realtime (speech-to-speech) session rather than
    /// by the client-driven speech-to-text plus text-to-speech cascade.
    /// </summary>
    public bool RealtimeEnabled { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the deployment that carries the realtime session, or
    /// <see langword="null"/> when there is no realtime session.
    /// </summary>
    public string RealtimeDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the deployment name the resource asked for, or <see langword="null"/> when it inherited
    /// the site default.
    /// </summary>
    public string RequestedDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets whether the resource named a conversation deployment that is not realtime-capable.
    /// </summary>
    /// <remarks>
    /// The realtime slot falls through to the site default when the name it is given does not qualify, which
    /// would turn a typo into a silent switch of model. A misconfigured resource should say so instead.
    /// </remarks>
    public bool IsMisconfigured { get; set; }

    /// <summary>
    /// Gets or sets whether this resolution came from folding a chat deployment that is itself a realtime
    /// model forward onto the conversation deployment.
    /// </summary>
    /// <remarks>
    /// True only for a resource stored before the conversation deployment existed. Editors use it to write the
    /// resource back in the current shape; see <c>ConversationModeResolutionExtensions</c> for why the fold
    /// cannot happen while deserializing.
    /// </remarks>
    public bool FoldedFromChatDeployment { get; set; }
}
