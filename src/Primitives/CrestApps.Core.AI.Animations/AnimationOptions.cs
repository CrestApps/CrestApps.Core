namespace CrestApps.Core.AI.Animations;

/// <summary>
/// Configures the system animation agent, the limits a render works within, and where videos are kept. Bound
/// from the <c>CrestApps:AI:Animations</c> configuration section.
/// </summary>
public sealed class AnimationOptions
{
    /// <summary>
    /// The configuration section the options are read from.
    /// </summary>
    public const string ConfigurationSectionName = "CrestApps:AI:Animations";

    /// <summary>
    /// Gets or sets a value indicating whether the system animation agent is offered to the model. Turning it
    /// off removes the agent and the description of it that every request carries. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the FFmpeg executable: a full path, or a name found on the <c>PATH</c>. Defaults to
    /// <c>ffmpeg</c>. The agent is not offered while no usable FFmpeg is found.
    /// </summary>
    public string FFmpegPath { get; set; } = "ffmpeg";

    /// <summary>
    /// Gets or sets the H.264 encoder FFmpeg is asked to use: <c>libx264</c>, <c>libopenh264</c> or
    /// <c>h264_mf</c>. When empty, the first of those the FFmpeg build offers is used.
    /// </summary>
    public string VideoEncoder { get; set; }

    /// <summary>
    /// Gets or sets the libx264 constant rate factor: lower is sharper and larger. Defaults to 20.
    /// </summary>
    public int ConstantRateFactor { get; set; } = 20;

    /// <summary>
    /// Gets or sets the bit rate, in kilobits per second, used by encoders that do not take a rate factor.
    /// Defaults to 6,000.
    /// </summary>
    public int BitRateKilobits { get; set; } = 6_000;

    /// <summary>
    /// Gets or sets the folder videos are stored under. A relative path is relative to the content root.
    /// Defaults to <c>App_Data/Animations</c>.
    /// </summary>
    public string BasePath { get; set; }

    /// <summary>
    /// Gets or sets the folder a render works in before the video is stored. Defaults to a folder under the
    /// system temporary folder.
    /// </summary>
    public string TempDirectory { get; set; }

    /// <summary>
    /// Gets or sets the frame width used when a request does not give one. Defaults to 1280.
    /// </summary>
    public int DefaultWidth { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the frame height used when a request does not give one. Defaults to 720.
    /// </summary>
    public int DefaultHeight { get; set; } = 720;

    /// <summary>
    /// Gets or sets the widest frame a request may ask for. Defaults to 1920.
    /// </summary>
    public int MaxWidth { get; set; } = 1920;

    /// <summary>
    /// Gets or sets the tallest frame a request may ask for. Defaults to 1080.
    /// </summary>
    public int MaxHeight { get; set; } = 1080;

    /// <summary>
    /// Gets or sets the frame rate used when a request does not give one. Defaults to 30.
    /// </summary>
    public int DefaultFrameRate { get; set; } = 30;

    /// <summary>
    /// Gets or sets the highest frame rate a request may ask for. Defaults to 60.
    /// </summary>
    public int MaxFrameRate { get; set; } = 60;

    /// <summary>
    /// Gets or sets the length, in seconds, used when a request does not give one. Defaults to 10.
    /// </summary>
    public double DefaultDurationSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets the longest video, in seconds, a request may ask for. Defaults to 30.
    /// </summary>
    public double MaxDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets how long one render may take before it is stopped. Defaults to two minutes.
    /// </summary>
    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets how many renders may run at once on this server. A render keeps a CPU core busy, so
    /// further requests wait their turn. Defaults to 2.
    /// </summary>
    public int MaxConcurrentRenders { get; set; } = 2;

    /// <summary>
    /// Gets or sets the largest video, in bytes, that is kept. Defaults to 50 MB.
    /// </summary>
    public long MaxOutputBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether a script's key frames are judged by the vision deployment before it is
    /// rendered — a first draft and one revision — and sent back to the agent with the director's critique when they
    /// fall short. Has no effect when no vision deployment is configured. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ReviewScriptsBeforeRender { get; set; } = true;

    /// <summary>
    /// Gets or sets the chat deployment the animation agent writes its drawing code with. A stronger model draws
    /// noticeably better. Empty, the default, uses the deployment of the chat that called the agent.
    /// </summary>
    public string AgentDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the longest script, in characters, the agent may submit. Defaults to 100,000.
    /// </summary>
    public int MaxScriptCharacters { get; set; } = 100_000;

    /// <summary>
    /// Gets or sets how long the script's setup, or one frame, may run. Defaults to two seconds.
    /// </summary>
    public TimeSpan ScriptFrameTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how many statements the script's setup, or one frame, may run. Defaults to 5,000,000.
    /// </summary>
    public int MaxScriptStatementsPerFrame { get; set; } = 5_000_000;

    /// <summary>
    /// Gets or sets how much memory, in bytes, the script's setup, or one frame, may allocate. Defaults to 256 MB.
    /// </summary>
    public long MaxScriptMemoryBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// Gets or sets how many drawing values one frame may record. Defaults to 2,000,000.
    /// </summary>
    public int MaxScriptCommandsPerFrame { get; set; } = 2_000_000;
}
