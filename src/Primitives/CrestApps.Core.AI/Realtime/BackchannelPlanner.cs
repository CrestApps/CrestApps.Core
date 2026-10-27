namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Decides when the AI should make a short acknowledging sound ("mm-hm") while the caller keeps talking, and which one.
/// </summary>
public sealed class BackchannelPlanner
{
    private static readonly string[] _defaultPhrases = ["Mm-hm.", "I see.", "Right.", "Okay."];

    private readonly string[] _phrases;
    private readonly int _wordsBetween;
    private int _lastWordCount;
    private int _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackchannelPlanner"/> class.
    /// </summary>
    /// <param name="phrases">The phrases to rotate through. A built-in set is used when empty.</param>
    /// <param name="wordsBetween">How many words the caller speaks between acknowledgements.</param>
    public BackchannelPlanner(IEnumerable<string> phrases = null, int wordsBetween = 18)
    {
        var configured = phrases?.Where(phrase => !string.IsNullOrWhiteSpace(phrase)).Select(phrase => phrase.Trim()).ToArray();

        _phrases = configured is { Length: > 0 } ? configured : _defaultPhrases;
        _wordsBetween = Math.Max(1, wordsBetween);
    }

    /// <summary>
    /// Starts a new caller turn.
    /// </summary>
    public void Reset()
    {
        _lastWordCount = 0;
    }

    /// <summary>
    /// Reports how many words the caller has spoken in the current turn and returns an acknowledgement when one is due.
    /// </summary>
    /// <param name="wordsInTurn">The words transcribed so far in this turn.</param>
    /// <param name="phrase">The phrase to speak.</param>
    public bool TryNext(int wordsInTurn, out string phrase)
    {
        if (wordsInTurn < _lastWordCount)
        {
            _lastWordCount = 0;
        }

        if (wordsInTurn - _lastWordCount < _wordsBetween)
        {
            phrase = null;

            return false;
        }

        _lastWordCount = wordsInTurn;
        phrase = _phrases[_next++ % _phrases.Length];

        return true;
    }
}
