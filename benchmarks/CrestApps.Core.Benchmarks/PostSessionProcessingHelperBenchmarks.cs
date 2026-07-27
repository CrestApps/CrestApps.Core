using System.Collections;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the legacy repeated message text projection with the retained single-read implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class PostSessionMessageTextBenchmarks
{
    private IReadOnlyList<ChatMessage> _messages;

    /// <summary>
    /// Gets or sets the number of transcript messages.
    /// </summary>
    [Params(10, 100, 1_000)]
    public int ItemCount { get; set; }

    /// <summary>
    /// Creates a realistic transcript and verifies exact candidate equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _messages = CreateMessages(ItemCount);

        foreach (var message in _messages)
        {
            var legacy = GetMessageTextLegacy(message);
            var current = GetMessageTextCurrent(message);

            if (!string.Equals(legacy, current, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The current message text helper changed the projected text.");
            }
        }

        if (ReadTranscriptLegacy() != ReadTranscriptCurrent())
        {
            throw new InvalidOperationException("The message text helpers produced different transcript lengths.");
        }
    }

    /// <summary>
    /// Reads every transcript message with the captured LINQ and concatenation implementation.
    /// </summary>
    /// <returns>The total projected text length.</returns>
    [Benchmark(Baseline = true)]
    public int ReadTranscriptLegacy()
    {
        var totalLength = 0;

        foreach (var message in _messages)
        {
            totalLength += GetMessageTextLegacy(message)?.Length ?? 0;
        }

        return totalLength;
    }

    /// <summary>
    /// Reads every transcript message by evaluating <see cref="ChatMessage.Text"/> once.
    /// </summary>
    /// <returns>The total projected text length.</returns>
    [Benchmark]
    public int ReadTranscriptCurrent()
    {
        var totalLength = 0;

        foreach (var message in _messages)
        {
            totalLength += GetMessageTextCurrent(message)?.Length ?? 0;
        }

        return totalLength;
    }

    /// <summary>
    /// Creates transcript messages that cover null, empty, text, non-text, and mixed content.
    /// </summary>
    /// <param name="itemCount">The number of messages to create.</param>
    /// <returns>The generated transcript messages.</returns>
    private static List<ChatMessage> CreateMessages(int itemCount)
    {
        var messages = new List<ChatMessage>(itemCount);

        for (var index = 0; index < itemCount; index++)
        {
            messages.Add((index % 10) switch
            {
                0 => null,
                1 => new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Contents = null,
                },
                2 => new ChatMessage
                {
                    Role = ChatRole.User,
                    Contents = [],
                },
                3 => new ChatMessage(ChatRole.Assistant, string.Empty),
                4 => new ChatMessage(ChatRole.User, " \t\r\n"),
                5 => new ChatMessage(
                    ChatRole.User,
                    $"Message {index}: I need help understanding the deployment and its latest status."),
                6 => new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        null,
                        new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
                        new TextContent($"Response {index}: The deployment is healthy."),
                        new TextContent("\nNext step: validate the production slot."),
                    ],
                },
                7 => new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        null,
                        new DataContent(new byte[] { 4, 5 }, "application/octet-stream"),
                    ],
                },
                8 => new ChatMessage
                {
                    Role = ChatRole.User,
                    Contents =
                    [
                        new TextContent(null),
                        new TextContent(string.Empty),
                        new TextContent($"Follow-up {index}: include the warning-free build results."),
                    ],
                },
                _ => new ChatMessage(
                    ChatRole.Assistant,
                    $"Final response {index}\r\nThe release build and focused tests passed."),
            });
        }

        return messages;
    }

    /// <summary>
    /// Projects message text with the captured repeated-read implementation.
    /// </summary>
    /// <param name="message">The message to project.</param>
    /// <returns>The usable message text, or <see langword="null"/>.</returns>
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

    /// <summary>
    /// Projects message text by reading <see cref="ChatMessage.Text"/> once.
    /// </summary>
    /// <param name="message">The message to project.</param>
    /// <returns>The usable message text, or <see langword="null"/>.</returns>
    private static string GetMessageTextCurrent(ChatMessage message)
    {
        var text = message?.Text;

        return string.IsNullOrWhiteSpace(text)
            ? null
            : text;
    }
}

/// <summary>
/// Compares the retained task-result projection with the rejected capped builder candidate.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class PostSessionTaskResultSummaryBenchmarks
{
    private IReadOnlyList<BenchmarkTaskResult> _results;

    /// <summary>
    /// Gets or sets the number of task results.
    /// </summary>
    [Params(10, 100, 1_000)]
    public int ItemCount { get; set; }

    /// <summary>
    /// Creates realistic task results and verifies exact candidate equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _results = CreateResults(ItemCount);

        var legacy = CreateTaskResultSummaryLegacy(_results);
        var candidate = CreateTaskResultSummaryBuilderCandidate(_results);

        if (!string.Equals(legacy, candidate, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The builder task result summary changed the formatted output.");
        }

        VerifyTaskResultSummarySemantics();
    }

    /// <summary>
    /// Formats task results with the captured LINQ projection and <see cref="string.Join(string?, IEnumerable{string?})"/>.
    /// </summary>
    /// <returns>The formatted task result summary.</returns>
    [Benchmark(Baseline = true)]
    public string CreateSummaryLegacy()
    {
        return CreateTaskResultSummaryLegacy(_results);
    }

    /// <summary>
    /// Formats task results with the rejected conservatively pre-sized single-pass builder.
    /// </summary>
    /// <returns>The formatted task result summary.</returns>
    [Benchmark]
    public string CreateSummaryBuilderCandidate()
    {
        return CreateTaskResultSummaryBuilderCandidate(_results);
    }

    /// <summary>
    /// Creates task results containing representative names and values.
    /// </summary>
    /// <param name="itemCount">The number of task results to create.</param>
    /// <returns>The generated task results.</returns>
    private static List<BenchmarkTaskResult> CreateResults(int itemCount)
    {
        var results = new List<BenchmarkTaskResult>(itemCount);

        for (var index = 0; index < itemCount; index++)
        {
            results.Add((index % 10) switch
            {
                0 => null,
                1 => new BenchmarkTaskResult(),
                2 => new BenchmarkTaskResult
                {
                    Name = string.Empty,
                    Value = string.Empty,
                },
                3 => new BenchmarkTaskResult
                {
                    Name = "  disposition  ",
                    Value = " \t\r\n",
                },
                4 => new BenchmarkTaskResult
                {
                    Name = $"summary_{index}",
                    Value = $"The customer asked about deployment status in transcript {index}.",
                },
                5 => new BenchmarkTaskResult
                {
                    Name = $"sentiment_{index}",
                    Value = "Positive",
                },
                6 => new BenchmarkTaskResult
                {
                    Name = $"follow_up_{index}",
                    Value = null,
                },
                7 => new BenchmarkTaskResult
                {
                    Name = $"error\n{index}",
                    Value = "failed",
                },
                8 => new BenchmarkTaskResult
                {
                    Name = $"routing_{index}",
                    Value = "Technical Support",
                },
                _ => new BenchmarkTaskResult
                {
                    Name = $"resolution_{index}",
                    Value = "Resolved",
                },
            });
        }

        return results;
    }

    /// <summary>
    /// Formats task results with the captured LINQ projection.
    /// </summary>
    /// <param name="results">The task results to format.</param>
    /// <returns>The formatted task result summary.</returns>
    private static string CreateTaskResultSummaryLegacy(IEnumerable<BenchmarkTaskResult> results)
    {
        if (results == null)
        {
            return "(none)";
        }

        var summaries = results.Select(result =>
            $"Name='{result?.Name ?? "(null)"}', HasValue={!string.IsNullOrWhiteSpace(result?.Value)}");

        return string.Join("; ", summaries);
    }

    /// <summary>
    /// Formats task results with the capped builder candidate.
    /// </summary>
    /// <param name="results">The task results to format.</param>
    /// <returns>The formatted task result summary.</returns>
    private static string CreateTaskResultSummaryBuilderCandidate(IEnumerable<BenchmarkTaskResult> results)
    {
        if (results == null)
        {
            return "(none)";
        }

        const int estimatedResultLength = 48;
        const int maximumInitialCapacity = 4 * 1024;
        var capacity = results.TryGetNonEnumeratedCount(out var count)
            ? Math.Min(count, maximumInitialCapacity / estimatedResultLength) * estimatedResultLength
            : 0;
        var builder = new StringBuilder(capacity);
        var isFirst = true;

        foreach (var result in results)
        {
            if (!isFirst)
            {
                builder.Append("; ");
            }

            builder
                .Append("Name='")
                .Append(result?.Name ?? "(null)")
                .Append("', HasValue=")
                .Append(!string.IsNullOrWhiteSpace(result?.Value));

            isFirst = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Verifies exact formatting and single-enumeration behavior for the builder candidate.
    /// </summary>
    private static void VerifyTaskResultSummarySemantics()
    {
        BenchmarkTaskResult[] results =
        [
            null,
            new(),
            new()
            {
                Name = string.Empty,
                Value = string.Empty,
            },
            new()
            {
                Name = "  spaced name  ",
                Value = " \t\r\n",
            },
            new()
            {
                Name = "line1\nline2",
                Value = "value",
            },
        ];
        var legacyResults = new TrackingEnumerable<BenchmarkTaskResult>(results);
        var candidateResults = new TrackingEnumerable<BenchmarkTaskResult>(results);
        var legacy = CreateTaskResultSummaryLegacy(legacyResults);
        var candidate = CreateTaskResultSummaryBuilderCandidate(candidateResults);
        const string expected = "Name='(null)', HasValue=False; Name='(null)', HasValue=False; " +
            "Name='', HasValue=False; Name='  spaced name  ', HasValue=False; " +
            "Name='line1\nline2', HasValue=True";

        if (!string.Equals(expected, legacy, StringComparison.Ordinal)
            || !string.Equals(expected, candidate, StringComparison.Ordinal)
            || legacyResults.EnumerationCount != 1
            || candidateResults.EnumerationCount != 1
            || CreateTaskResultSummaryLegacy([]) != string.Empty
            || CreateTaskResultSummaryBuilderCandidate([]) != string.Empty
            || CreateTaskResultSummaryLegacy(null) != "(none)"
            || CreateTaskResultSummaryBuilderCandidate(null) != "(none)")
        {
            throw new InvalidOperationException("The task result summary candidate changed enumeration or formatting semantics.");
        }
    }

    /// <summary>
    /// Represents a task result used by the benchmark.
    /// </summary>
    private sealed class BenchmarkTaskResult
    {
        /// <summary>
        /// Gets or sets the task name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the task value.
        /// </summary>
        public string Value { get; set; }
    }

    /// <summary>
    /// Tracks how many times an enumerable is enumerated.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    private sealed class TrackingEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _items;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingEnumerable{T}"/> class.
        /// </summary>
        /// <param name="items">The items to enumerate.</param>
        public TrackingEnumerable(IEnumerable<T> items)
        {
            _items = items;
        }

        /// <summary>
        /// Gets the number of enumerations.
        /// </summary>
        public int EnumerationCount { get; private set; }

        /// <summary>
        /// Returns an enumerator and records the enumeration.
        /// </summary>
        /// <returns>An enumerator for the wrapped items.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;

            return _items.GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

/// <summary>
/// Compares the unchanged prompt projection with direct-loop candidates.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class PostSessionPromptProjectionBenchmarks
{
    private IReadOnlyList<AIChatSessionPrompt> _prompts;

    /// <summary>
    /// Gets or sets the number of prompts.
    /// </summary>
    [Params(10, 100, 1_000)]
    public int ItemCount { get; set; }

    /// <summary>
    /// Creates a realistic prompt sequence and verifies exact candidate equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _prompts = CreatePrompts(ItemCount);

        var legacy = ProjectPromptsLegacy(_prompts);
        var current = ProjectPromptsCurrent(_prompts);
        var loop = ProjectPromptsLoopCandidate(_prompts);
        var preSizedLoop = ProjectPromptsPreSizedLoopCandidate(_prompts);
        var legacyJson = JsonSerializer.Serialize(legacy);

        if (!string.Equals(legacyJson, JsonSerializer.Serialize(current), StringComparison.Ordinal)
            || !string.Equals(legacyJson, JsonSerializer.Serialize(loop), StringComparison.Ordinal)
            || !string.Equals(legacyJson, JsonSerializer.Serialize(preSizedLoop), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The prompt projection candidates changed the serialized shape.");
        }
    }

    /// <summary>
    /// Projects prompts with the captured LINQ filter, projection, and materialization.
    /// </summary>
    /// <returns>The projected prompts.</returns>
    [Benchmark(Baseline = true)]
    public List<object> ProjectPromptsLegacy()
    {
        return ProjectPromptsLegacy(_prompts);
    }

    /// <summary>
    /// Projects prompts with the unchanged production LINQ implementation.
    /// </summary>
    /// <returns>The projected prompts.</returns>
    [Benchmark]
    public List<object> ProjectPromptsCurrent()
    {
        return ProjectPromptsCurrent(_prompts);
    }

    /// <summary>
    /// Projects prompts with a direct loop and default list growth.
    /// </summary>
    /// <returns>The projected prompts.</returns>
    [Benchmark]
    public List<object> ProjectPromptsLoopCandidate()
    {
        return ProjectPromptsLoopCandidate(_prompts);
    }

    /// <summary>
    /// Projects prompts with a direct loop and source-count list capacity.
    /// </summary>
    /// <returns>The projected prompts.</returns>
    [Benchmark]
    public List<object> ProjectPromptsPreSizedLoopCandidate()
    {
        return ProjectPromptsPreSizedLoopCandidate(_prompts);
    }

    /// <summary>
    /// Creates prompts containing representative roles, text, and generated entries.
    /// </summary>
    /// <param name="itemCount">The number of prompts to create.</param>
    /// <returns>The generated prompts.</returns>
    private static List<AIChatSessionPrompt> CreatePrompts(int itemCount)
    {
        var prompts = new List<AIChatSessionPrompt>(itemCount);

        for (var index = 0; index < itemCount; index++)
        {
            prompts.Add(new AIChatSessionPrompt
            {
                Role = (index % 5) switch
                {
                    0 => ChatRole.User,
                    1 => ChatRole.Assistant,
                    2 => ChatRole.System,
                    3 => ChatRole.User,
                    _ => ChatRole.Assistant,
                },
                Content = (index % 8) switch
                {
                    0 => null,
                    1 => string.Empty,
                    2 => " \t\r\n",
                    3 => $"  User message {index}: please summarize the deployment.  ",
                    4 => $"Assistant response {index}: the deployment is healthy.\r\n",
                    5 => $"Line one for prompt {index}.\nLine two contains the next action.",
                    6 => "No additional details.",
                    _ => $"Prompt {index}: retain exact ordering and role projection.",
                },
                IsGeneratedPrompt = index % 7 == 0,
                Title = $"Ignored title {index}",
            });
        }

        return prompts;
    }

    /// <summary>
    /// Projects prompts with the captured LINQ implementation.
    /// </summary>
    /// <param name="prompts">The prompts to project.</param>
    /// <returns>The projected prompt objects.</returns>
    private static List<object> ProjectPromptsLegacy(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        return prompts
            .Where(prompt => !prompt.IsGeneratedPrompt)
            .Select(prompt => (object)new Dictionary<string, object>
            {
                ["Role"] = prompt.Role == ChatRole.User ? "User" : "Assistant",
                ["Content"] = prompt.Content?.Trim(),
            })
            .ToList();
    }

    /// <summary>
    /// Projects prompts with the unchanged production implementation.
    /// </summary>
    /// <param name="prompts">The prompts to project.</param>
    /// <returns>The projected prompt objects.</returns>
    private static List<object> ProjectPromptsCurrent(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        return prompts
            .Where(prompt => !prompt.IsGeneratedPrompt)
            .Select(prompt => (object)new Dictionary<string, object>
            {
                ["Role"] = prompt.Role == ChatRole.User ? "User" : "Assistant",
                ["Content"] = prompt.Content?.Trim(),
            })
            .ToList();
    }

    /// <summary>
    /// Projects prompts with a direct loop and default list growth.
    /// </summary>
    /// <param name="prompts">The prompts to project.</param>
    /// <returns>The projected prompt objects.</returns>
    private static List<object> ProjectPromptsLoopCandidate(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts, "source");

        return ProjectPrompts(prompts, new List<object>());
    }

    /// <summary>
    /// Projects prompts with a direct loop and source-count list capacity.
    /// </summary>
    /// <param name="prompts">The prompts to project.</param>
    /// <returns>The projected prompt objects.</returns>
    private static List<object> ProjectPromptsPreSizedLoopCandidate(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts, "source");

        return ProjectPrompts(prompts, new List<object>(prompts.Count));
    }

    /// <summary>
    /// Projects prompts into the supplied destination list.
    /// </summary>
    /// <param name="prompts">The prompts to project.</param>
    /// <param name="projectedPrompts">The destination list.</param>
    /// <returns>The populated destination list.</returns>
    private static List<object> ProjectPrompts(
        IReadOnlyList<AIChatSessionPrompt> prompts,
        List<object> projectedPrompts)
    {
        foreach (var prompt in prompts)
        {
            if (prompt.IsGeneratedPrompt)
            {
                continue;
            }

            projectedPrompts.Add(new Dictionary<string, object>
            {
                ["Role"] = prompt.Role == ChatRole.User ? "User" : "Assistant",
                ["Content"] = prompt.Content?.Trim(),
            });
        }

        return projectedPrompts;
    }
}
