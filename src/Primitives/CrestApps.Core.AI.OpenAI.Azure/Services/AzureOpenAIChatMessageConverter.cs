using Microsoft.Extensions.AI;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using SdkChatMessage = OpenAI.Chat.ChatMessage;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// Converts framework chat messages into Azure OpenAI SDK messages while preserving the adapter's
/// role, content, ordering, exception, and history-count semantics.
/// </summary>
internal static class AzureOpenAIChatMessageConverter
{
    private const int MaximumInitialTailCapacity = 256;

    /// <summary>
    /// Converts raw messages and bounds conversion to the requested eligible-message tail when applicable.
    /// </summary>
    /// <param name="messages">The raw framework messages.</param>
    /// <param name="pastMessagesCount">The configured converted-message history count.</param>
    /// <returns>The converted Azure OpenAI SDK messages.</returns>
    public static List<SdkChatMessage> Convert(
        IEnumerable<AIChatMessage> messages,
        int? pastMessagesCount)
    {
        return pastMessagesCount > 1
            ? ConvertBounded(messages, pastMessagesCount.Value)
            : ConvertAll(messages);
    }

    /// <summary>
    /// Converts one user message using the adapter's existing content and fallback rules.
    /// </summary>
    /// <param name="message">The raw user message.</param>
    /// <returns>The converted SDK message, or <see langword="null"/> when no supported content is present.</returns>
    public static UserChatMessage CreateUserMessage(AIChatMessage message)
    {
        var builder = new ImmediateUserMessageBuilder();
        UserChatMessage userMessage;

        return TryCreateUserMessage(message, ref builder, out userMessage)
            ? userMessage
            : null;
    }

    /// <summary>
    /// Converts every eligible raw message immediately.
    /// </summary>
    /// <param name="messages">The raw framework messages.</param>
    /// <returns>The converted SDK messages.</returns>
    private static List<SdkChatMessage> ConvertAll(IEnumerable<AIChatMessage> messages)
    {
        var converted = new List<SdkChatMessage>();
        var currentPrompt = string.Empty;

        foreach (var message in messages)
        {
            if (message.Role == AIChatRole.User)
            {
                var userMessage = CreateUserMessage(message);

                if (userMessage != null)
                {
                    converted.Add(userMessage);
                    currentPrompt = message.Text;
                }
            }
            else if (message.Role == AIChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
            {
                converted.Add(new AssistantChatMessage(message.Text));
            }
        }

        _ = currentPrompt;

        return converted;
    }

    /// <summary>
    /// Classifies every raw message in encounter order and converts only the retained eligible tail.
    /// </summary>
    /// <param name="messages">The raw framework messages.</param>
    /// <param name="pastMessagesCount">The positive tail capacity.</param>
    /// <returns>The converted retained SDK messages.</returns>
    private static List<SdkChatMessage> ConvertBounded(
        IEnumerable<AIChatMessage> messages,
        int pastMessagesCount)
    {
        var tail = new List<PendingChatMessage>(
            Math.Min(pastMessagesCount, MaximumInitialTailCapacity));
        var nextIndex = 0;

        foreach (var message in messages)
        {
            if (!TryCreatePendingChatMessage(message, out var pendingMessage))
            {
                continue;
            }

            if (tail.Count < pastMessagesCount)
            {
                tail.Add(pendingMessage);
            }
            else
            {
                tail[nextIndex] = pendingMessage;
                nextIndex++;

                if (nextIndex == pastMessagesCount)
                {
                    nextIndex = 0;
                }
            }
        }

        var converted = new List<SdkChatMessage>(tail.Count);

        for (var index = nextIndex; index < tail.Count; index++)
        {
            converted.Add(tail[index].Create());
        }

        for (var index = 0; index < nextIndex; index++)
        {
            converted.Add(tail[index].Create());
        }

        return converted;
    }

    /// <summary>
    /// Classifies one raw message while performing the same observable raw-content reads as immediate conversion.
    /// </summary>
    /// <param name="message">The raw message.</param>
    /// <param name="pendingMessage">The pending converted message.</param>
    /// <returns><see langword="true"/> when the message produces an SDK message.</returns>
    private static bool TryCreatePendingChatMessage(
        AIChatMessage message,
        out PendingChatMessage pendingMessage)
    {
        if (message.Role == AIChatRole.User)
        {
            if (!TryCreatePendingUserMessage(message, out var userMessage))
            {
                pendingMessage = default;

                return false;
            }

            _ = message.Text;
            pendingMessage = PendingChatMessage.CreateUser(userMessage);

            return true;
        }

        if (message.Role == AIChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
        {
            pendingMessage = PendingChatMessage.CreateAssistant(message.Text);

            return true;
        }

        pendingMessage = default;

        return false;
    }

    /// <summary>
    /// Classifies one user message and captures immutable text and owned image bytes at encounter time.
    /// </summary>
    /// <param name="message">The raw user message.</param>
    /// <param name="pendingMessage">The pending user message.</param>
    /// <returns><see langword="true"/> when supported content or fallback text is present.</returns>
    private static bool TryCreatePendingUserMessage(
        AIChatMessage message,
        out PendingUserMessage pendingMessage)
    {
        var builder = new PendingUserMessageBuilder();

        return TryCreateUserMessage(message, ref builder, out pendingMessage);
    }

    /// <summary>
    /// Applies the shared user-message classification to an immediate or deferred message builder.
    /// </summary>
    /// <typeparam name="TBuilder">The user-message builder type.</typeparam>
    /// <typeparam name="TMessage">The resulting immediate or pending message type.</typeparam>
    /// <param name="message">The raw user message.</param>
    /// <param name="builder">The user-message builder.</param>
    /// <param name="convertedMessage">The converted immediate or pending user message.</param>
    /// <returns><see langword="true"/> when supported content or fallback text is present.</returns>
    private static bool TryCreateUserMessage<TBuilder, TMessage>(
        AIChatMessage message,
        ref TBuilder builder,
        out TMessage convertedMessage)
        where TBuilder : struct, IUserMessageBuilder<TMessage>
    {
        if (message.Contents is not { Count: > 0 })
        {
            return TryCreateTextOnlyUserMessage(
                message,
                ref builder,
                out convertedMessage);
        }

        builder.BeginContent();

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent textContent when !string.IsNullOrWhiteSpace(textContent.Text):
                    builder.AddText(textContent.Text);
                    break;
                case DataContent dataContent when
                    dataContent.Data is { Length: > 0 } &&
                    !string.IsNullOrWhiteSpace(dataContent.MediaType):
                    builder.AddImage(
                        BinaryData.FromBytes(dataContent.Data.ToArray()),
                        dataContent.MediaType);
                    break;
            }
        }

        if (builder.Count == 0)
        {
            return TryCreateTextOnlyUserMessage(
                message,
                ref builder,
                out convertedMessage);
        }

        convertedMessage = builder.Build();

        return true;
    }

    /// <summary>
    /// Applies the user-message text fallback with the same two text reads as immediate conversion.
    /// </summary>
    /// <param name="message">The raw user message.</param>
    /// <typeparam name="TBuilder">The user-message builder type.</typeparam>
    /// <typeparam name="TMessage">The resulting immediate or pending message type.</typeparam>
    /// <param name="builder">The user-message builder.</param>
    /// <param name="convertedMessage">The converted text-only user message.</param>
    /// <returns><see langword="true"/> when non-whitespace fallback text is present.</returns>
    private static bool TryCreateTextOnlyUserMessage<TBuilder, TMessage>(
        AIChatMessage message,
        ref TBuilder builder,
        out TMessage convertedMessage)
        where TBuilder : struct, IUserMessageBuilder<TMessage>
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            convertedMessage = default;

            return false;
        }

        convertedMessage = builder.BuildText(message.Text);

        return true;
    }

    /// <summary>
    /// Defines the pending SDK message kind.
    /// </summary>
    private enum PendingChatMessageKind
    {
        User,
        Assistant,
    }

    /// <summary>
    /// Defines the pending user-content kind.
    /// </summary>
    private enum PendingContentKind
    {
        Text,
        Image,
    }

    /// <summary>
    /// Builds either an immediate SDK user message or a deferred pending user message from shared classification.
    /// </summary>
    /// <typeparam name="TMessage">The resulting user-message type.</typeparam>
    private interface IUserMessageBuilder<TMessage>
    {
        /// <summary>
        /// Gets the number of supported content parts.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Starts processing an explicit content collection.
        /// </summary>
        void BeginContent();

        /// <summary>
        /// Adds one supported text part.
        /// </summary>
        /// <param name="text">The captured text.</param>
        void AddText(string text);

        /// <summary>
        /// Adds one supported image part.
        /// </summary>
        /// <param name="imageBytes">The owned image bytes.</param>
        /// <param name="mediaType">The image media type.</param>
        void AddImage(BinaryData imageBytes, string mediaType);

        /// <summary>
        /// Builds a message from explicit content parts.
        /// </summary>
        /// <returns>The built message.</returns>
        TMessage Build();

        /// <summary>
        /// Builds a text-only fallback message.
        /// </summary>
        /// <param name="text">The fallback text.</param>
        /// <returns>The built message.</returns>
        TMessage BuildText(string text);
    }

    /// <summary>
    /// Stores one classified message until its final tail membership is known.
    /// </summary>
    private readonly struct PendingChatMessage
    {
        private readonly PendingChatMessageKind _kind;
        private readonly PendingUserMessage _userMessage;
        private readonly string _assistantText;

        /// <summary>
        /// Initializes a new instance of the <see cref="PendingChatMessage"/> struct.
        /// </summary>
        /// <param name="kind">The pending message kind.</param>
        /// <param name="userMessage">The pending user message.</param>
        /// <param name="assistantText">The assistant text.</param>
        private PendingChatMessage(
            PendingChatMessageKind kind,
            PendingUserMessage userMessage,
            string assistantText)
        {
            _kind = kind;
            _userMessage = userMessage;
            _assistantText = assistantText;
        }

        /// <summary>
        /// Creates a pending user message.
        /// </summary>
        /// <param name="message">The classified user message.</param>
        /// <returns>The pending chat message.</returns>
        public static PendingChatMessage CreateUser(PendingUserMessage message)
        {
            return new PendingChatMessage(
                PendingChatMessageKind.User,
                message,
                assistantText: null);
        }

        /// <summary>
        /// Creates a pending assistant message.
        /// </summary>
        /// <param name="text">The captured assistant text.</param>
        /// <returns>The pending chat message.</returns>
        public static PendingChatMessage CreateAssistant(string text)
        {
            return new PendingChatMessage(
                PendingChatMessageKind.Assistant,
                userMessage: default,
                text);
        }

        /// <summary>
        /// Creates the Azure OpenAI SDK message.
        /// </summary>
        /// <returns>The converted SDK message.</returns>
        public SdkChatMessage Create()
        {
            return _kind == PendingChatMessageKind.User
                ? _userMessage.Create()
                : new AssistantChatMessage(_assistantText);
        }
    }

    /// <summary>
    /// Stores one classified user message without creating eager SDK image data URIs.
    /// </summary>
    private readonly struct PendingUserMessage
    {
        private readonly string _text;
        private readonly PendingContent _singleContent;
        private readonly List<PendingContent> _contents;
        private readonly int _contentCount;

        /// <summary>
        /// Initializes a new text-only pending user message.
        /// </summary>
        /// <param name="text">The captured fallback text.</param>
        private PendingUserMessage(string text)
        {
            _text = text;
            _singleContent = default;
            _contents = null;
            _contentCount = 0;
        }

        /// <summary>
        /// Initializes a new single-part pending user message.
        /// </summary>
        /// <param name="content">The pending content part.</param>
        private PendingUserMessage(PendingContent content)
        {
            _text = null;
            _singleContent = content;
            _contents = null;
            _contentCount = 1;
        }

        /// <summary>
        /// Initializes a new multi-part pending user message.
        /// </summary>
        /// <param name="contents">The pending content parts.</param>
        private PendingUserMessage(List<PendingContent> contents)
        {
            _text = null;
            _singleContent = default;
            _contents = contents;
            _contentCount = contents.Count;
        }

        /// <summary>
        /// Creates a text-only pending user message.
        /// </summary>
        /// <param name="text">The captured fallback text.</param>
        /// <returns>The pending user message.</returns>
        public static PendingUserMessage CreateText(string text)
        {
            return new PendingUserMessage(text);
        }

        /// <summary>
        /// Creates a single-part pending user message.
        /// </summary>
        /// <param name="content">The pending content part.</param>
        /// <returns>The pending user message.</returns>
        public static PendingUserMessage CreateSingle(PendingContent content)
        {
            return new PendingUserMessage(content);
        }

        /// <summary>
        /// Creates a multi-part pending user message.
        /// </summary>
        /// <param name="contents">The pending content parts.</param>
        /// <returns>The pending user message.</returns>
        public static PendingUserMessage CreateMultiple(List<PendingContent> contents)
        {
            return new PendingUserMessage(contents);
        }

        /// <summary>
        /// Creates the Azure OpenAI SDK user message.
        /// </summary>
        /// <returns>The converted SDK user message.</returns>
        public UserChatMessage Create()
        {
            if (_contentCount == 0)
            {
                return new UserChatMessage(_text);
            }

            if (_contentCount == 1)
            {
                return new UserChatMessage(_singleContent.Create());
            }

            var parts = new List<ChatMessageContentPart>(_contentCount);

            foreach (var content in _contents)
            {
                parts.Add(content.Create());
            }

            return new UserChatMessage(parts);
        }
    }

    /// <summary>
    /// Builds an SDK user message immediately for unbounded history settings.
    /// </summary>
    private struct ImmediateUserMessageBuilder : IUserMessageBuilder<UserChatMessage>
    {
        private List<ChatMessageContentPart> _parts;

        /// <summary>
        /// Gets the number of converted content parts.
        /// </summary>
        public readonly int Count => _parts?.Count ?? 0;

        /// <summary>
        /// Allocates the content-part list at the same point as the legacy converter.
        /// </summary>
        public void BeginContent()
        {
            _parts = [];
        }

        /// <summary>
        /// Converts and adds one text part immediately.
        /// </summary>
        /// <param name="text">The captured text.</param>
        public readonly void AddText(string text)
        {
            _parts.Add(ChatMessageContentPart.CreateTextPart(text));
        }

        /// <summary>
        /// Converts and adds one image part immediately.
        /// </summary>
        /// <param name="imageBytes">The owned image bytes.</param>
        /// <param name="mediaType">The image media type.</param>
        public readonly void AddImage(
            BinaryData imageBytes,
            string mediaType)
        {
            _parts.Add(ChatMessageContentPart.CreateImagePart(
                imageBytes,
                mediaType));
        }

        /// <summary>
        /// Builds the SDK user message from explicit content parts.
        /// </summary>
        /// <returns>The converted SDK user message.</returns>
        public readonly UserChatMessage Build()
        {
            return new UserChatMessage(_parts);
        }

        /// <summary>
        /// Builds the SDK user message from fallback text.
        /// </summary>
        /// <param name="text">The fallback text.</param>
        /// <returns>The converted SDK user message.</returns>
        public readonly UserChatMessage BuildText(string text)
        {
            return new UserChatMessage(text);
        }
    }

    /// <summary>
    /// Builds a pending user message while avoiding a list allocation for a single supported part.
    /// </summary>
    private struct PendingUserMessageBuilder : IUserMessageBuilder<PendingUserMessage>
    {
        private PendingContent _singleContent;
        private List<PendingContent> _contents;

        /// <summary>
        /// Gets the number of supported content parts.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Starts processing explicit content.
        /// </summary>
        public readonly void BeginContent()
        {
        }

        /// <summary>
        /// Adds one pending text part.
        /// </summary>
        /// <param name="text">The captured text.</param>
        public void AddText(string text)
        {
            Add(PendingContent.CreateText(text));
        }

        /// <summary>
        /// Adds one pending image part.
        /// </summary>
        /// <param name="imageBytes">The owned image bytes.</param>
        /// <param name="mediaType">The image media type.</param>
        public void AddImage(
            BinaryData imageBytes,
            string mediaType)
        {
            Add(PendingContent.CreateImage(imageBytes, mediaType));
        }

        /// <summary>
        /// Adds one pending content part.
        /// </summary>
        /// <param name="content">The pending content part.</param>
        private void Add(PendingContent content)
        {
            if (Count == 0)
            {
                _singleContent = content;
            }
            else
            {
                _contents ??= [_singleContent];
                _contents.Add(content);
            }

            Count++;
        }

        /// <summary>
        /// Builds the pending user message.
        /// </summary>
        /// <returns>The pending user message.</returns>
        public readonly PendingUserMessage Build()
        {
            return Count == 1
                ? PendingUserMessage.CreateSingle(_singleContent)
                : PendingUserMessage.CreateMultiple(_contents);
        }

        /// <summary>
        /// Builds a text-only pending user message.
        /// </summary>
        /// <param name="text">The fallback text.</param>
        /// <returns>The pending user message.</returns>
        public readonly PendingUserMessage BuildText(string text)
        {
            return PendingUserMessage.CreateText(text);
        }
    }

    /// <summary>
    /// Stores one supported user content part until SDK conversion is required.
    /// </summary>
    private readonly struct PendingContent
    {
        private readonly PendingContentKind _kind;
        private readonly string _text;
        private readonly BinaryData _imageBytes;
        private readonly string _mediaType;

        /// <summary>
        /// Initializes a new pending content part.
        /// </summary>
        /// <param name="kind">The content kind.</param>
        /// <param name="text">The text content.</param>
        /// <param name="imageBytes">The owned image bytes.</param>
        /// <param name="mediaType">The image media type.</param>
        private PendingContent(
            PendingContentKind kind,
            string text,
            BinaryData imageBytes,
            string mediaType)
        {
            _kind = kind;
            _text = text;
            _imageBytes = imageBytes;
            _mediaType = mediaType;
        }

        /// <summary>
        /// Creates a pending text part.
        /// </summary>
        /// <param name="text">The captured text.</param>
        /// <returns>The pending content part.</returns>
        public static PendingContent CreateText(string text)
        {
            return new PendingContent(
                PendingContentKind.Text,
                text,
                imageBytes: null,
                mediaType: null);
        }

        /// <summary>
        /// Creates a pending image part.
        /// </summary>
        /// <param name="imageBytes">The owned image bytes.</param>
        /// <param name="mediaType">The image media type.</param>
        /// <returns>The pending content part.</returns>
        public static PendingContent CreateImage(
            BinaryData imageBytes,
            string mediaType)
        {
            return new PendingContent(
                PendingContentKind.Image,
                text: null,
                imageBytes,
                mediaType);
        }

        /// <summary>
        /// Creates the Azure OpenAI SDK content part.
        /// </summary>
        /// <returns>The converted SDK content part.</returns>
        public ChatMessageContentPart Create()
        {
            return _kind == PendingContentKind.Text
                ? ChatMessageContentPart.CreateTextPart(_text)
                : ChatMessageContentPart.CreateImagePart(_imageBytes, _mediaType);
        }
    }
}
