using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Folds the usage reported across the updates of a streamed response without keeping the updates themselves,
/// so streaming large audio does not hold every chunk in memory just to meter it.
/// </summary>
internal sealed class AIStreamingUsageAccumulator
{
    private UsageDetails _usage;

    /// <summary>
    /// Gets a value indicating whether any update was seen.
    /// </summary>
    public bool HasUpdates { get; private set; }

    /// <summary>
    /// Gets the last model id an update reported.
    /// </summary>
    public string ModelId { get; private set; }

    /// <summary>
    /// Gets the last response id an update reported.
    /// </summary>
    public string ResponseId { get; private set; }

    /// <summary>
    /// Gets the usage summed across every update, or <see langword="null"/> when none reported usage.
    /// </summary>
    public UsageDetails Usage => _usage;

    /// <summary>
    /// Adds one update.
    /// </summary>
    /// <param name="modelId">The model id the update reported.</param>
    /// <param name="responseId">The response id the update reported.</param>
    /// <param name="contents">The update's contents, which may carry <see cref="UsageContent"/>.</param>
    public void Add(string modelId, string responseId, IEnumerable<AIContent> contents)
    {
        HasUpdates = true;

        if (!string.IsNullOrEmpty(modelId))
        {
            ModelId = modelId;
        }

        if (!string.IsNullOrEmpty(responseId))
        {
            ResponseId = responseId;
        }

        if (contents is null)
        {
            return;
        }

        foreach (var content in contents)
        {
            if (content is UsageContent { Details: { } details })
            {
                _usage ??= new UsageDetails();
                _usage.Add(details);
            }
        }
    }
}
