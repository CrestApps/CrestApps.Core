using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Resilience;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Support.Json;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// System tool that generates Chart.js configuration JSON from a data description.
/// Returns the chart config in the <c>[chart:json]</c> format recognized by the client.
/// </summary>
public sealed class GenerateChartTool : AIFunction
{
    public const string TheName = SystemToolNames.GenerateChart;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "labels": {
          "type": "array",
          "items": { "type": "string" },
          "description": "The category labels along the axis, one per data point. ALWAYS supply this together with 'series' when you know the actual values; it is exact and cannot fail."
        },
        "series": {
          "type": "array",
          "description": "The data plotted against 'labels'. Each series holds one value per label, in the same order.",
          "items": {
            "type": "object",
            "properties": {
              "name": { "type": "string", "description": "The series name shown in the legend." },
              "values": {
                "type": "array",
                "items": { "type": ["number", "null"] },
                "description": "Numeric values aligned with 'labels'. Use null for a missing point."
              }
            },
            "required": ["values"],
            "additionalProperties": false
          }
        },
        "chart_type": {
          "type": "string",
          "description": "The chart type. Defaults to 'bar'.",
          "enum": ["bar", "line", "pie", "doughnut", "radar", "polarArea", "scatter"]
        },
        "title": { "type": "string", "description": "The chart title." },
        "horizontal": { "type": "boolean", "description": "Draw a bar chart horizontally, which is easier to read when the labels are long." },
        "color_by_sign": { "type": "boolean", "description": "Color each bar green when the value is positive and red when negative. Use this for variance and change charts." },
        "stacked": { "type": "boolean", "description": "Stack the series instead of drawing them side by side." },
        "data_description": {
          "type": "string",
          "description": "A prose description of the data, used ONLY as a fallback when the actual values are not available to you. Supplying 'labels' and 'series' instead is strongly preferred: prose has to be re-derived by another model, which loses precision and can fail on large data sets."
        }
      },
      "required": [],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "REQUIRED for any chart or data visualization request. This is the ONLY way to render a visual chart in the UI. Do NOT generate chart JSON inline - it will not be rendered. Always call this tool instead. Whenever you know the actual numbers, pass them as 'labels' and 'series' rather than describing them in 'data_description': the chart is then built exactly from your values and cannot fail. Returns a special [chart:JSON] marker that MUST be included exactly as-is in your response.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>()
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Invoke cores core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<GenerateChartTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        // Building from the caller's own values is exact and cannot fail, so it is tried first and the
        // model-assisted path below is only a fallback for callers that have prose and nothing else.
        if (TryBuildFromSuppliedData(arguments, out var builtConfiguration))
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' built a chart directly from supplied data.", Name);
            }

            return BuildSuccessResponse(builtConfiguration);
        }

        if (!arguments.TryGetFirstString("data_description", out var dataDescription))
        {
            logger.LogWarning("AI tool '{ToolName}' was called without chart data.", Name);

            return "No chart data was supplied. Pass 'labels' with the category names and 'series' with the numeric values, or 'data_description' describing the data to plot.";
        }

        try
        {
            var executionContext = AIInvocationScope.Current?.ToolExecutionContext;

            if (executionContext is null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: execution context is missing.", Name);

                return $"Chart generation is not available. The {nameof(AIToolExecutionContext)} is missing from the invocation context.";
            }

            var deploymentManager = arguments.Services.GetRequiredService<IAIDeploymentManager>();

            var deployment = await deploymentManager.ResolveUtilityOrDefaultAsync(
                clientName: executionContext.ClientName);

            if (deployment == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no chat model deployment configured.", Name);

                return "Chart generation is not available. No chat model deployment is configured.";
            }

            var aIClientFactory = arguments.Services.GetRequiredService<IAIClientFactory>();

            var chatClient = await aIClientFactory.CreateChatClientAsync(
                deployment,
                builder => builder.UseDefaultResilience());

            if (chatClient == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: resolved deployment did not produce a chat client.", Name);

                return "Chart generation is not available. The resolved deployment could not create a chat client.";
            }

            var promptService = arguments.Services.GetService<ITemplateService>();

            var systemPrompt = promptService != null
                ? await promptService.RenderAsync(AITemplateIds.ChartGeneration, cancellationToken: cancellationToken)
                : string.Empty;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt ?? string.Empty),
                new(ChatRole.User, dataDescription),
            };

            var chatOptions = new ChatOptions
            {
                Temperature = 0.3f,

                // A chart with a few dozen categories and several series runs well past a couple of
                // thousand tokens. Truncated output parses as invalid JSON, which is indistinguishable
                // from a failure to the caller, so the ceiling is high enough to finish the object.
                MaxOutputTokens = 8000,
            };

            var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

            if (response is null || string.IsNullOrWhiteSpace(response.Text))
            {
                logger.LogWarning("AI tool '{ToolName}' received an empty response from the chat client.", Name);

                return "The chart model returned no configuration. Retry by calling this tool again with the actual values in 'labels' and 'series' instead of a prose description.";
            }

            var chartConfig = JsonExtractor.ExtractJsonObject(response.Text);

            if (string.IsNullOrEmpty(chartConfig))
            {
                logger.LogWarning("AI tool '{ToolName}' failed to extract valid JSON from the chat response.", Name);

                return "The chart configuration could not be parsed, most likely because the description was too large to convert reliably. Retry by calling this tool again with the actual values in 'labels' and 'series' instead of a prose description.";
            }

            // Validate it's valid Chart.js JSON.
            try
            {
                using var doc = JsonDocument.Parse(chartConfig);

                if (!doc.RootElement.TryGetProperty("type", out _) || !doc.RootElement.TryGetProperty("data", out _))
                {
                    logger.LogWarning("AI tool '{ToolName}' generated chart config missing 'type' or 'data' properties.", Name);

                    return "The generated chart configuration was incomplete. Retry by calling this tool again with the actual values in 'labels' and 'series' instead of a prose description.";
                }
            }
            catch (JsonException)
            {
                logger.LogWarning("AI tool '{ToolName}' generated invalid JSON for chart configuration.", Name);

                return "The chart configuration could not be parsed, most likely because the description was too large to convert reliably. Retry by calling this tool again with the actual values in 'labels' and 'series' instead of a prose description.";
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return BuildSuccessResponse(chartConfig);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during chart generation.");

            return "An error occurred while generating the chart. Retry by calling this tool again with the actual values in 'labels' and 'series' instead of a prose description.";
        }
    }

    private static string BuildSuccessResponse(string chartConfig)
    {
        return $"Chart generated successfully. Include the following marker exactly as-is in your response (do NOT modify, convert to an image, or replace it):\n\n[chart:{chartConfig}]";
    }

    /// <summary>
    /// Builds the chart configuration from values the caller supplied directly.
    /// </summary>
    /// <param name="arguments">The tool arguments.</param>
    /// <param name="configuration">The built configuration.</param>
    /// <returns><see langword="true"/> when the caller supplied enough data to plot.</returns>
    private static bool TryBuildFromSuppliedData(AIFunctionArguments arguments, out string configuration)
    {
        configuration = null;

        if (!TryGetElement(arguments, "labels", out var labelsElement) ||
            labelsElement.ValueKind != JsonValueKind.Array ||
            !TryGetElement(arguments, "series", out var seriesElement) ||
            seriesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var labels = new List<string>();

        foreach (var label in labelsElement.EnumerateArray())
        {
            labels.Add(label.ValueKind switch
            {
                JsonValueKind.String => label.GetString(),
                JsonValueKind.Number => label.GetRawText(),
                _ => string.Empty,
            });
        }

        if (labels.Count == 0)
        {
            return false;
        }

        var series = new List<ChartConfigurationBuilder.ChartSeries>();

        foreach (var item in seriesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("values", out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = new List<double?>();

            foreach (var value in valuesElement.EnumerateArray())
            {
                values.Add(value.ValueKind switch
                {
                    JsonValueKind.Number when value.TryGetDouble(out var number) => number,
                    JsonValueKind.String when double.TryParse(
                        value.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var parsed) => parsed,
                    _ => null,
                });
            }

            var name = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            series.Add(new ChartConfigurationBuilder.ChartSeries(name, values));
        }

        if (series.Count == 0)
        {
            return false;
        }

        arguments.TryGetFirstString("chart_type", out var chartType);
        arguments.TryGetFirstString("title", out var title);

        configuration = ChartConfigurationBuilder.Build(
            chartType,
            title,
            labels,
            series,
            GetBoolean(arguments, "horizontal"),
            GetBoolean(arguments, "color_by_sign"),
            GetBoolean(arguments, "stacked"));

        return true;
    }

    private static bool TryGetElement(AIFunctionArguments arguments, string name, out JsonElement element)
    {
        element = default;

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return false;
        }

        if (value is JsonElement existing)
        {
            element = existing;

            return true;
        }

        // Arguments do not always arrive as JSON elements; re-serializing gives one shape to read.
        try
        {
            element = JsonSerializer.SerializeToElement(value);

            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool GetBoolean(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            bool flag => flag,
            string text => bool.TryParse(text, out var parsed) && parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.String } element => bool.TryParse(element.GetString(), out var parsed) && parsed,
            _ => false,
        };
    }
}
