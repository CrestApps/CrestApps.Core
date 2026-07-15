using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class SecurityPromptOrchestrationHandlerBehaviorTests
{
    private const string SecurityPreambleTemplateId = "security-preamble";

    /// <summary>
    /// Verifies an empty builder receives only the rendered preamble without separator newlines.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_EmptyBuilder_AppendsPreambleDirectly()
    {
        const string preamble = "security preamble";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext();
        var builder = orchestrationContext.SystemMessageBuilder;
        builder.EnsureCapacity(4096);
        var initialCapacity = builder.Capacity;
        var builtContext = new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext);

        await handler.BuiltAsync(builtContext, TestContext.Current.CancellationToken);

        Assert.Same(orchestrationContext, builtContext.OrchestrationContext);
        Assert.Same(builder, orchestrationContext.SystemMessageBuilder);
        Assert.Same(orchestrationContext.CompletionContext, builtContext.OrchestrationContext.CompletionContext);
        Assert.Equal(initialCapacity, builder.Capacity);
        AssertOrdinalEqual(preamble, builder.ToString());
    }

    /// <summary>
    /// Verifies a builder populated by one append receives the exact preamble and platform separators.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_OneExistingAppend_PrependsExactOrdinalText()
    {
        const string preamble = "preamble";
        const string existing = "existing system message";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext();
        var builder = orchestrationContext.SystemMessageBuilder;
        builder.EnsureCapacity(existing.Length);
        builder.Append(existing);
        Assert.Equal(1, CountChunks(builder));
        var builtContext = new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext);

        await handler.BuiltAsync(builtContext, TestContext.Current.CancellationToken);

        Assert.Same(builder, orchestrationContext.SystemMessageBuilder);
        AssertOrdinalEqual(CreateExpected(preamble, existing), builder.ToString());
    }

    /// <summary>
    /// Verifies a chunked builder receives the same exact output as a contiguous builder.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_ManyExistingAppends_PrependsExactOrdinalText()
    {
        const string preamble = "preamble";
        var existing = string.Concat(Enumerable.Repeat("0123456789abcdef", 64));
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext();
        var builder = orchestrationContext.SystemMessageBuilder;

        foreach (var chunk in Enumerable.Repeat("0123456789abcdef", 64))
        {
            builder.Append(chunk);
        }

        Assert.True(CountChunks(builder) > 1);
        var builtContext = new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext);

        await handler.BuiltAsync(builtContext, TestContext.Current.CancellationToken);

        Assert.Same(builder, orchestrationContext.SystemMessageBuilder);
        AssertOrdinalEqual(CreateExpected(preamble, existing), builder.ToString());
    }

    /// <summary>
    /// Verifies leading and trailing newline code units in the existing message remain unchanged.
    /// </summary>
    /// <param name="existing">The existing system-message text.</param>
    [Theory]
    [InlineData("\nleading LF")]
    [InlineData("\rleading CR")]
    [InlineData("\r\nleading CRLF")]
    [InlineData("trailing LF\n")]
    [InlineData("trailing CR\r")]
    [InlineData("trailing CRLF\r\n")]
    [InlineData("\r\nsurrounded\n")]
    public async Task BuiltAsync_ExistingLeadingOrTrailingNewlines_PreservesEveryCodeUnit(string existing)
    {
        const string preamble = "preamble";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        AssertOrdinalEqual(CreateExpected(preamble, existing), orchestrationContext.SystemMessageBuilder.ToString());
    }

    /// <summary>
    /// Verifies null, empty, and whitespace-only template results leave builder state unchanged.
    /// </summary>
    /// <param name="preamble">The rendered preamble result.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00A0\u2003")]
    public async Task BuiltAsync_NullEmptyOrWhitespacePreamble_PreservesBuilder(string preamble)
    {
        const string existing = "existing";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext();
        var builder = orchestrationContext.SystemMessageBuilder;
        builder.EnsureCapacity(8192);
        builder.Append(existing);
        var initialCapacity = builder.Capacity;
        var initialChunkCount = CountChunks(builder);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        Assert.Equal(initialCapacity, builder.Capacity);
        Assert.Equal(initialChunkCount, CountChunks(builder));
        AssertOrdinalEqual(existing, builder.ToString());
    }

    /// <summary>
    /// Verifies a large rendered preamble is prepended without truncation or normalization.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_LargePreamble_PreservesExactOrdinalText()
    {
        var preamble = string.Concat(new string('p', 1024 * 1024), "\uD83D\uDE00", "\uD800");
        const string existing = "tail";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        AssertOrdinalEqual(CreateExpected(preamble, existing), orchestrationContext.SystemMessageBuilder.ToString());
    }

    /// <summary>
    /// Verifies a whitespace-only existing message is treated as non-empty and receives separators.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_WhitespaceExistingMessage_IsTreatedAsNonEmpty()
    {
        const string preamble = "preamble";
        const string existing = " \t\r\n\u00A0";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        AssertOrdinalEqual(CreateExpected(preamble, existing), orchestrationContext.SystemMessageBuilder.ToString());
    }

    /// <summary>
    /// Verifies carriage returns, line feeds, CRLF pairs, and platform newlines are not normalized.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_MixedNewlineSequences_PreservesExactOrdinalText()
    {
        var preamble = $"pre\rpre\npre\r\npre{Environment.NewLine}end";
        var existing = $"existing\nexisting\rexisting\r\nexisting{Environment.NewLine}end";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        var actual = orchestrationContext.SystemMessageBuilder.ToString();
        AssertOrdinalEqual(CreateExpected(preamble, existing), actual);
        AssertOrdinalEqual(
            string.Concat(preamble, Environment.NewLine, Environment.NewLine, existing),
            actual);
    }

    /// <summary>
    /// Verifies Unicode scalar values, valid surrogate pairs, and lone surrogates remain unchanged.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_UnicodeAndLoneSurrogates_PreservesUtf16CodeUnits()
    {
        const string preamble = "préamble—漢字\uD83D\uDE00|\uD800|\uDC00";
        const string existing = "\uDC00existing\uD83D\uDE80\uD800tail";
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        AssertOrdinalEqual(CreateExpected(preamble, existing), orchestrationContext.SystemMessageBuilder.ToString());
    }

    /// <summary>
    /// Verifies a one-megabyte existing message is prepended without truncation or content changes.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_OneMegabyteExistingMessage_PreservesExactOrdinalText()
    {
        const string preamble = "preamble";
        var existing = string.Concat(
            new string('x', (1024 * 1024) - 6),
            "\r\n",
            "\uD83D\uDE00",
            "\uD800",
            "\uDC00");
        var handler = CreateHandler(preamble);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken);

        AssertOrdinalEqual(CreateExpected(preamble, existing), orchestrationContext.SystemMessageBuilder.ToString());
    }

    /// <summary>
    /// Verifies repeated handler invocation prepends another preamble and separator sequence each time.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_RepeatedInvocation_PrependsOnEveryCall()
    {
        const string preamble = "preamble";
        const string existing = "existing";
        var templateService = CreateTemplateService(preamble);
        var handler = CreateHandler(templateService.Object);
        var orchestrationContext = CreateOrchestrationContext(existing);
        var builtContext = new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext);

        await handler.BuiltAsync(builtContext, TestContext.Current.CancellationToken);
        await handler.BuiltAsync(builtContext, TestContext.Current.CancellationToken);

        var once = CreateExpected(preamble, existing);
        AssertOrdinalEqual(CreateExpected(preamble, once), orchestrationContext.SystemMessageBuilder.ToString());
        templateService.Verify(
            service => service.RenderAsync(
                SecurityPreambleTemplateId,
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Verifies a template exception is propagated unchanged before any builder mutation.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_TemplateException_PropagatesAndPreservesBuilder()
    {
        const string existing = "existing";
        var expectedException = new InvalidOperationException("template failure");
        var templateService = new Mock<ITemplateService>(MockBehavior.Strict);
        templateService
            .Setup(service => service.RenderAsync(
                SecurityPreambleTemplateId,
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<string>(expectedException));
        var handler = CreateHandler(templateService.Object);
        var orchestrationContext = CreateOrchestrationContext(existing);
        var builder = orchestrationContext.SystemMessageBuilder;
        var initialCapacity = builder.Capacity;

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            TestContext.Current.CancellationToken));

        Assert.Same(expectedException, actualException);
        Assert.Equal(initialCapacity, builder.Capacity);
        AssertOrdinalEqual(existing, builder.ToString());
    }

    /// <summary>
    /// Verifies template cancellation uses the supplied token and occurs before builder mutation.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_TemplateCancellation_PropagatesAndPreservesBuilder()
    {
        const string existing = "existing";
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellationToken = cancellationSource.Token;
        var observedToken = default(CancellationToken);
        var templateService = new Mock<ITemplateService>(MockBehavior.Strict);
        templateService
            .Setup(service => service.RenderAsync(
                SecurityPreambleTemplateId,
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, _, token) => observedToken = token)
            .Returns(Task.FromCanceled<string>(cancellationToken));
        var handler = CreateHandler(templateService.Object);
        var orchestrationContext = CreateOrchestrationContext(existing);
        var builder = orchestrationContext.SystemMessageBuilder;
        var initialCapacity = builder.Capacity;

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            cancellationToken));

        Assert.Equal(cancellationToken, observedToken);
        Assert.Equal(cancellationToken, exception.CancellationToken);
        Assert.Equal(initialCapacity, builder.Capacity);
        AssertOrdinalEqual(existing, builder.ToString());
    }

    /// <summary>
    /// Verifies the handler does not independently reject a canceled token when rendering completes.
    /// </summary>
    [Fact]
    public async Task BuiltAsync_CanceledTokenIgnoredByTemplate_ComposesNormally()
    {
        const string preamble = "preamble";
        const string existing = "existing";
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellationToken = cancellationSource.Token;
        var observedToken = default(CancellationToken);
        var templateService = CreateTemplateService(preamble);
        templateService
            .Setup(service => service.RenderAsync(
                SecurityPreambleTemplateId,
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, _, token) => observedToken = token)
            .ReturnsAsync(preamble);
        var handler = CreateHandler(templateService.Object);
        var orchestrationContext = CreateOrchestrationContext(existing);

        await handler.BuiltAsync(
            new OrchestrationContextBuiltContext(new AIProfile(), orchestrationContext),
            cancellationToken);

        Assert.Equal(cancellationToken, observedToken);
        AssertOrdinalEqual(CreateExpected(preamble, existing), orchestrationContext.SystemMessageBuilder.ToString());
    }

    /// <summary>
    /// Creates the exact legacy expected output for a rendered preamble and existing message.
    /// </summary>
    /// <param name="preamble">The rendered preamble.</param>
    /// <param name="existing">The existing builder content.</param>
    /// <returns>The exact composed output.</returns>
    private static string CreateExpected(string preamble, string existing)
    {
        return string.IsNullOrEmpty(existing)
            ? preamble
            : string.Concat(preamble, Environment.NewLine, Environment.NewLine, existing);
    }

    /// <summary>
    /// Creates a handler with a fixed rendered preamble.
    /// </summary>
    /// <param name="preamble">The rendered preamble result.</param>
    /// <returns>The configured handler.</returns>
    private static SecurityPromptOrchestrationHandler CreateHandler(string preamble)
    {
        return CreateHandler(CreateTemplateService(preamble).Object);
    }

    /// <summary>
    /// Creates a handler with the supplied template service and preamble-only security options.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    /// <returns>The configured handler.</returns>
    private static SecurityPromptOrchestrationHandler CreateHandler(ITemplateService templateService)
    {
        return new SecurityPromptOrchestrationHandler(
            templateService,
            Options.Create(new PromptSecurityOptions
            {
                EnableSecurityPreamble = true,
                EnableInputDelimiters = false,
                EnableInjectionDetection = false,
                EnableOutputFiltering = false,
            }),
            NullLogger<SecurityPromptOrchestrationHandler>.Instance);
    }

    /// <summary>
    /// Creates a strict template service that returns a fixed preamble.
    /// </summary>
    /// <param name="preamble">The rendered preamble result.</param>
    /// <returns>The configured template-service mock.</returns>
    private static Mock<ITemplateService> CreateTemplateService(string preamble)
    {
        var templateService = new Mock<ITemplateService>(MockBehavior.Strict);
        templateService
            .Setup(service => service.RenderAsync(
                SecurityPreambleTemplateId,
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(preamble);

        return templateService;
    }

    /// <summary>
    /// Creates an orchestration context with a completion context and optional existing system message.
    /// </summary>
    /// <param name="existing">The existing system-message content.</param>
    /// <returns>The configured orchestration context.</returns>
    private static OrchestrationContext CreateOrchestrationContext(string existing = null)
    {
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
        };

        if (existing is not null)
        {
            context.SystemMessageBuilder.Append(existing);
        }

        return context;
    }

    /// <summary>
    /// Counts the current chunks in a string builder without materializing them.
    /// </summary>
    /// <param name="builder">The builder to inspect.</param>
    /// <returns>The number of chunks.</returns>
    private static int CountChunks(StringBuilder builder)
    {
        var count = 0;

        foreach (var chunk in builder.GetChunks())
        {
            _ = chunk;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Asserts two strings contain the same UTF-16 code units in ordinal order.
    /// </summary>
    /// <param name="expected">The expected text.</param>
    /// <param name="actual">The actual text.</param>
    private static void AssertOrdinalEqual(string expected, string actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }
}
