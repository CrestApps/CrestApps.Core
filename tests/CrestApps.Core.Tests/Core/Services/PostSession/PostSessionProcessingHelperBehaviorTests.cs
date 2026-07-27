using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Services.PostSession;

public sealed class PostSessionProcessingHelperBehaviorTests
{
    [Fact]
    public void SingleReadMessageTextProjection_ShouldMatchLegacyForNullNonTextAndMixedContents()
    {
        ChatMessage[] messages =
        [
            null,
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents = null,
            },
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents =
                [
                    null,
                    new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
                ],
            },
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents =
                [
                    new TextContent(null),
                    new TextContent(string.Empty),
                    new TextContent(" \t\r\n"),
                ],
            },
            new ChatMessage
            {
                Role = ChatRole.Assistant,
                Contents =
                [
                    null,
                    new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
                    new TextContent("alpha"),
                    new TextContent("|beta"),
                    new DataContent(new byte[] { 4, 5 }, "application/octet-stream"),
                    new TextContent("\ngamma"),
                ],
            },
            new ChatMessage(ChatRole.Assistant, "  first line\r\nsecond line\t "),
        ];

        var legacy = messages.Select(GetMessageTextLegacy).ToArray();
        var singleRead = messages.Select(GetMessageTextSingleRead).ToArray();
        string[] expected =
        [
            null,
            null,
            null,
            null,
            "alpha|beta\ngamma",
            "  first line\r\nsecond line\t ",
        ];

        Assert.Equal(legacy, singleRead);
        Assert.Equal(expected, singleRead);
    }

    private static string GetMessageTextLegacy(ChatMessage message)
    {
        if (message == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }

        var contentText = string.Concat(message.Contents?.OfType<TextContent>().Select(content => content.Text) ?? []);

        return string.IsNullOrWhiteSpace(contentText)
            ? null
            : contentText;
    }

    private static string GetMessageTextSingleRead(ChatMessage message)
    {
        var text = message?.Text;

        return string.IsNullOrWhiteSpace(text)
            ? null
            : text;
    }
}
