using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// The charts the conversation already showed the reader, read from the <c>[chart:…]</c> markers of the
/// earlier answers.
/// </summary>
/// <remarks>
/// The agent is handed only the request the assistant wrote for it, never the conversation, so "put the
/// charts I approved in the PDF" reached it with no data: it added chart blocks that had nothing to plot,
/// and the document came out with headings over empty space. Reading the charts from the stored answers lets
/// the agent use the exact chart the user saw, by number, instead of depending on the assistant to copy a
/// long Chart.js configuration into its request.
/// </remarks>
internal static class PdfConversationCharts
{
    private const string CacheKey = nameof(PdfConversationCharts);

    /// <summary>
    /// Reads the charts shown in the conversation the current call runs in, oldest first.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The charts; empty outside a conversation.</returns>
    public static async Task<IReadOnlyList<PdfChartDefinition>> GetAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            return [];
        }

        if (invocation.Items.TryGetValue(CacheKey, out var cached) && cached is IReadOnlyList<PdfChartDefinition> charts)
        {
            return charts;
        }

        var texts = await ReadAnswersAsync(services, invocation);
        var found = new List<PdfChartDefinition>();

        foreach (var text in texts)
        {
            foreach (var marker in PdfChartJsReader.FindMarkers(text))
            {
                if (PdfChartJsReader.TryRead(marker, out var chart))
                {
                    found.Add(chart);
                }
            }
        }

        invocation.Items[CacheKey] = found;

        return found;
    }

    /// <summary>
    /// Describes the charts so the model can pick one by number.
    /// </summary>
    /// <param name="charts">The charts.</param>
    /// <returns>One line per chart, or <see langword="null"/> when there are none.</returns>
    public static string Describe(IReadOnlyList<PdfChartDefinition> charts)
    {
        if (charts is not { Count: > 0 })
        {
            return null;
        }

        var builder = new StringBuilder("Charts shown earlier in the conversation (use one with chart.conversation_chart):");

        for (var index = 0; index < charts.Count; index++)
        {
            var chart = charts[index];
            var labels = chart.Labels ?? [];

            builder.AppendLine()
                .Append(index + 1).Append(". ")
                .Append(string.IsNullOrWhiteSpace(chart.Title) ? "(untitled)" : $"\"{chart.Title}\"")
                .Append(" — ").Append(chart.ChartType)
                .Append(", ").Append(labels.Count.ToString(CultureInfo.InvariantCulture)).Append(" categories");

            if (labels.Count > 0)
            {
                builder.Append(" (").Append(string.Join(", ", labels.Take(3)));

                if (labels.Count > 3)
                {
                    builder.Append(", …");
                }

                builder.Append(')');
            }

            var names = (chart.Series ?? []).Where(series => !string.IsNullOrWhiteSpace(series.Name)).Select(series => series.Name).ToList();

            if (names.Count > 0)
            {
                builder.Append("; series: ").Append(string.Join(", ", names));
            }
        }

        return builder.ToString();
    }

    private static async Task<IEnumerable<string>> ReadAnswersAsync(IServiceProvider services, AIInvocationContext invocation)
    {
        switch (invocation.ToolExecutionContext?.Resource)
        {
            case ChatInteraction interaction when !string.IsNullOrEmpty(interaction.ItemId):
                {
                    var store = services.GetService<IChatInteractionPromptStore>();

                    if (store is null)
                    {
                        return [];
                    }

                    var prompts = await store.GetPromptsAsync(interaction.ItemId);

                    return prompts
                        .Where(prompt => prompt.Role == ChatRole.Assistant)
                        .OrderBy(prompt => prompt.CreatedUtc)
                        .Select(prompt => prompt.Text);
                }

            case AIProfile:
                {
                    var session = invocation.ChatSession ??
                        (invocation.Items.TryGetValue(nameof(AIChatSession), out var value) ? value as AIChatSession : null);
                    var store = services.GetService<IAIChatSessionPromptStore>();

                    if (string.IsNullOrEmpty(session?.SessionId) || store is null)
                    {
                        return [];
                    }

                    var prompts = await store.GetPromptsAsync(session.SessionId);

                    return prompts
                        .Where(prompt => prompt.Role == ChatRole.Assistant)
                        .OrderBy(prompt => prompt.CreatedUtc)
                        .Select(prompt => prompt.Content);
                }

            default:
                return [];
        }
    }
}
