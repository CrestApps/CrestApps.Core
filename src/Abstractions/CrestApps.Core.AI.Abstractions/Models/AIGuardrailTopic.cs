namespace CrestApps.Core.AI.Models;

/// <summary>
/// A guardrail topic a profile refuses, read from its template.
/// </summary>
/// <param name="Id">The template identifier, which the utility model names in its answer.</param>
/// <param name="Title">The topic's title.</param>
/// <param name="Definition">What falls under the topic.</param>
public sealed record AIGuardrailTopic(string Id, string Title, string Definition);
