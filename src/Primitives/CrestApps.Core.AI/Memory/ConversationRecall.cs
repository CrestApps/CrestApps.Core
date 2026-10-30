using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Finds the turns of a conversation that bear on a question, by the terms they share with it. Used to bring
/// older turns of a long conversation back into view, and by the conversation-search tools.
/// </summary>
internal static class ConversationRecall
{
    // A message longer than this is cut when quoted back, so one long answer cannot crowd out the prompt.
    public const int MaxExcerptLength = 1500;

    /// <summary>
    /// Gets the user and assistant messages that carry text, which are the ones the completion clients count
    /// against the past-messages window.
    /// </summary>
    public static List<ChatMessage> GetEligibleMessages(IEnumerable<ChatMessage> history)
    {
        return history?
            .Where(message => (message.Role == ChatRole.User || message.Role == ChatRole.Assistant) && !string.IsNullOrWhiteSpace(message.Text))
            .ToList() ?? [];
    }

    /// <summary>
    /// Groups messages into turns: a user message and the assistant messages that answer it. Turn numbers count
    /// from the start of <paramref name="messages"/>, starting at 1.
    /// </summary>
    public static List<ConversationTurn> GetTurns(IReadOnlyList<ChatMessage> messages)
    {
        var turns = new List<ConversationTurn>();
        ConversationTurn current = null;

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            if (message.Role == ChatRole.User || current is null)
            {
                current = new ConversationTurn { Number = turns.Count + 1, StartIndex = index };
                turns.Add(current);
            }

            if (message.Role == ChatRole.User)
            {
                current.UserText = Truncate(message.Text);
            }
            else
            {
                current.AssistantText = current.AssistantText is null
                    ? Truncate(message.Text)
                    : Truncate($"{current.AssistantText}\n{message.Text}");
            }
        }

        return turns;
    }

    /// <summary>
    /// Ranks <paramref name="turns"/> by the share of a query's terms they contain, best first, then most recent
    /// first. A turn must share at least two terms with a query, or every term of a one-term query.
    /// </summary>
    public static List<ConversationTurn> Search(
        ITextTokenizer tokenizer,
        IEnumerable<ConversationTurn> turns,
        IEnumerable<string> queries,
        int top,
        float minScore = 0.25f)
    {
        var queryTerms = queries?
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(tokenizer.Tokenize)
            .Where(terms => terms.Count > 0)
            .ToArray() ?? [];

        if (queryTerms.Length == 0)
        {
            return [];
        }

        var matches = new List<ConversationTurn>();

        foreach (var turn in turns)
        {
            var turnTerms = tokenizer.Tokenize($"{turn.UserText}\n{turn.AssistantText}");

            if (turnTerms.Count == 0)
            {
                continue;
            }

            var best = 0f;

            foreach (var terms in queryTerms)
            {
                var matched = terms.Count(turnTerms.Contains);

                if (matched < Math.Min(2, terms.Count))
                {
                    continue;
                }

                best = Math.Max(best, (float)matched / terms.Count);
            }

            if (best >= minScore)
            {
                matches.Add(turn with { Score = best });
            }
        }

        return matches
            .OrderByDescending(turn => turn.Score)
            .ThenByDescending(turn => turn.Number)
            .Take(top)
            .ToList();
    }

    public static string Truncate(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxExcerptLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, MaxExcerptLength), "…");
    }
}

/// <summary>
/// One turn of a conversation: what the user said and what the assistant answered.
/// </summary>
internal sealed record ConversationTurn
{
    public int Number { get; init; }

    /// <summary>
    /// Gets the index of the turn's first message in the list it was built from.
    /// </summary>
    public int StartIndex { get; init; }

    public string UserText { get; set; }

    public string AssistantText { get; set; }

    public float Score { get; init; }
}
