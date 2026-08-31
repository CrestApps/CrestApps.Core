namespace CrestApps.Core.AI.Models;

/// <summary>
/// Well-known technical names of the model features registered by the framework.
/// Modules can register additional features using <see cref="AIDeploymentCapabilityOptions.AddFeature"/>.
/// </summary>
public static class AIDeploymentFeatureNames
{
    /// <summary>
    /// The model can call tools or functions supplied with the request.
    /// </summary>
    public const string ToolCalling = "toolCalling";

    /// <summary>
    /// The model can return responses that conform to a supplied JSON schema.
    /// </summary>
    public const string StructuredOutputs = "structuredOutputs";

    /// <summary>
    /// The model can stream response updates as they are produced.
    /// </summary>
    public const string Streaming = "streaming";

    /// <summary>
    /// The model performs internal reasoning before producing an answer.
    /// </summary>
    public const string Reasoning = "reasoning";

    /// <summary>
    /// The model can understand image inputs (vision).
    /// </summary>
    public const string ImageInput = "imageInput";

    /// <summary>
    /// The model can generate images.
    /// </summary>
    public const string ImageOutput = "imageOutput";

    /// <summary>
    /// The model accepts audio input.
    /// </summary>
    public const string AudioInput = "audioInput";

    /// <summary>
    /// The model produces audio output.
    /// </summary>
    public const string AudioOutput = "audioOutput";

    /// <summary>
    /// The model can understand video inputs.
    /// </summary>
    public const string VideoInput = "videoInput";

    /// <summary>
    /// The model can generate video.
    /// </summary>
    public const string VideoOutput = "videoOutput";

    /// <summary>
    /// The model supports real-time, bidirectional speech-to-speech sessions. A deployment that declares
    /// this feature on a <see cref="AIDeploymentPurpose.Chat"/> model is eligible to run realtime sessions.
    /// </summary>
    public const string Realtime = "realtime";
}
