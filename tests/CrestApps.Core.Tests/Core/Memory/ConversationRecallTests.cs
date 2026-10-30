using CrestApps.Core.AI;
using CrestApps.Core.AI.Memory;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Memory;

public sealed class ConversationRecallTests
{
    private static readonly LuceneTextTokenizer _tokenizer = new();

    [Fact]
    public void GetTurns_PairsEachUserMessageWithTheAnswersThatFollow()
    {
        var turns = ConversationRecall.GetTurns(
        [
            new ChatMessage(ChatRole.Assistant, "Welcome!"),
            new ChatMessage(ChatRole.User, "first question"),
            new ChatMessage(ChatRole.Assistant, "first answer"),
            new ChatMessage(ChatRole.Assistant, "more of the first answer"),
            new ChatMessage(ChatRole.User, "second question"),
        ]);

        Assert.Equal(3, turns.Count);
        Assert.Equal((1, 0, null, "Welcome!"), (turns[0].Number, turns[0].StartIndex, turns[0].UserText, turns[0].AssistantText));
        Assert.Equal((2, 1, "first question", "first answer\nmore of the first answer"), (turns[1].Number, turns[1].StartIndex, turns[1].UserText, turns[1].AssistantText));
        Assert.Equal((3, 4, "second question", null), (turns[2].Number, turns[2].StartIndex, turns[2].UserText, turns[2].AssistantText));
    }

    [Fact]
    public void Search_RanksTurnsSharingMoreOfTheQuestion_AndIgnoresOneWordCoincidences()
    {
        var turns = ConversationRecall.GetTurns(
        [
            new ChatMessage(ChatRole.User, "Our project budget is 40000 dollars."),
            new ChatMessage(ChatRole.Assistant, "Noted: a budget of 40000 dollars."),
            new ChatMessage(ChatRole.User, "The deadline is in March."),
            new ChatMessage(ChatRole.Assistant, "Got it."),
            new ChatMessage(ChatRole.User, "Use the budget template."),
            new ChatMessage(ChatRole.Assistant, "Okay."),
        ]);

        var matches = ConversationRecall.Search(_tokenizer, turns, ["What budget in dollars did I give you?"], top: 3);

        // Turn 3 shares only "budget", which is not enough on its own for a multi-term question.
        var match = Assert.Single(matches);
        Assert.Equal(1, match.Number);
    }

    [Fact]
    public void Truncate_CutsLongExcerpts()
    {
        var text = new string('a', ConversationRecall.MaxExcerptLength + 50);

        Assert.Equal(ConversationRecall.MaxExcerptLength + 1, ConversationRecall.Truncate(text).Length);
        Assert.Equal("short", ConversationRecall.Truncate("short"));
    }
}
