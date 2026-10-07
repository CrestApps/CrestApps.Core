namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Well-known values for <see cref="Models.AICompletionUsageRecord.Purpose"/>: why the framework itself made a
/// request. Hosts and custom features may record any other value through <see cref="AIUsageScope"/>.
/// </summary>
public static class AIUsagePurposes
{
    /// <summary>
    /// Producing the assistant's reply in a conversation.
    /// </summary>
    public const string Conversation = "Conversation";

    /// <summary>
    /// Planning which tools and steps a request needs before answering it.
    /// </summary>
    public const string Planning = "Planning";

    /// <summary>
    /// Rewriting the user's message into a search query before retrieval.
    /// </summary>
    public const string SearchQueryGeneration = "SearchQueryGeneration";

    /// <summary>
    /// Embedding a query to search documents, data sources, or memory.
    /// </summary>
    public const string Retrieval = "Retrieval";

    /// <summary>
    /// Embedding content so it can be stored in a search index.
    /// </summary>
    public const string Indexing = "Indexing";

    /// <summary>
    /// Writing a title for a conversation.
    /// </summary>
    public const string TitleGeneration = "TitleGeneration";

    /// <summary>
    /// An agent answering a task another profile delegated to it.
    /// </summary>
    public const string SubAgent = "SubAgent";

    /// <summary>
    /// Processing the rows of tabular data in batches.
    /// </summary>
    public const string TabularProcessing = "TabularProcessing";

    /// <summary>
    /// Extracting structured data from a conversation.
    /// </summary>
    public const string DataExtraction = "DataExtraction";

    /// <summary>
    /// Running the tasks configured to process a conversation after it closes.
    /// </summary>
    public const string PostSessionProcessing = "PostSessionProcessing";

    /// <summary>
    /// Describing or analyzing an image.
    /// </summary>
    public const string ImageAnalysis = "ImageAnalysis";

    /// <summary>
    /// Generating an image.
    /// </summary>
    public const string ImageGeneration = "ImageGeneration";

    /// <summary>
    /// Generating a chart.
    /// </summary>
    public const string ChartGeneration = "ChartGeneration";

    /// <summary>
    /// Extracting metadata from an ingested publication.
    /// </summary>
    public const string MetadataExtraction = "MetadataExtraction";

    /// <summary>
    /// Transcribing a user's speech.
    /// </summary>
    public const string Transcription = "Transcription";

    /// <summary>
    /// Speaking a reply aloud.
    /// </summary>
    public const string SpeechSynthesis = "SpeechSynthesis";

    /// <summary>
    /// Holding a realtime voice conversation.
    /// </summary>
    public const string VoiceConversation = "VoiceConversation";
}
