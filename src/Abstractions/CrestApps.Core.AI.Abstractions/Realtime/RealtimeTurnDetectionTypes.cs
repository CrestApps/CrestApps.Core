namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// The turn-detection algorithms a realtime provider may support.
/// </summary>
public static class RealtimeTurnDetectionTypes
{
    /// <summary>
    /// Ends the user's turn after a fixed stretch of silence. Fast, but a pause for thought mid-sentence ends the
    /// turn and the model answers half a question.
    /// </summary>
    public const string ServerVad = "server_vad";

    /// <summary>
    /// Ends the user's turn when the model judges the utterance complete, so a natural pause inside a sentence does
    /// not trigger a reply. This is what makes a spoken conversation feel like one.
    /// </summary>
    public const string SemanticVad = "semantic_vad";
}
