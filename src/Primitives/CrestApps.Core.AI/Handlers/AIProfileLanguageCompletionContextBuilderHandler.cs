using System.Globalization;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Adds the reply-language instruction to the system message of a profile that configures
/// <see cref="AIProfileLanguageSettings"/>. The conversation's current language comes from
/// <see cref="AICompletionContextKeys.ConversationLanguage"/> (set by the host from a conversation variable or a
/// detected caller language) and otherwise from the profile's starting language.
/// </summary>
internal sealed class AIProfileLanguageCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        return Task.CompletedTask;
    }

    public Task BuiltAsync(AICompletionContextBuiltContext context)
    {
        if (context.Resource is not AIProfile profile || !profile.TryGetSettings<AIProfileLanguageSettings>(out var settings))
        {
            return Task.CompletedTask;
        }

        context.Context.AdditionalProperties.TryGetValue(AICompletionContextKeys.ConversationLanguage, out var variable);

        var current = DefaultConversationLanguageService.Match(settings, variable as string)
            ?? (string.IsNullOrWhiteSpace(settings.StartingLanguage) ? null : settings.StartingLanguage.Trim());

        var instruction = BuildInstruction(current, settings.AllowedLanguages);

        if (instruction is null)
        {
            return Task.CompletedTask;
        }

        context.Context.AdditionalProperties[AICompletionContextKeys.ConversationLanguage] = current;
        context.Context.SystemMessage = string.IsNullOrWhiteSpace(context.Context.SystemMessage)
            ? instruction
            : $"{context.Context.SystemMessage}\n\n{instruction}";

        return Task.CompletedTask;
    }

    internal static string BuildInstruction(string current, IReadOnlyCollection<string> allowed)
    {
        var allowedNames = (allowed ?? [])
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(DisplayName)
            .ToList();

        if (string.IsNullOrWhiteSpace(current) && allowedNames.Count == 0)
        {
            return null;
        }

        var instruction = string.IsNullOrWhiteSpace(current)
            ? "Reply in the language the user is speaking to you in."
            : $"Reply in {DisplayName(current)}.";

        if (allowedNames.Count > 0)
        {
            instruction += $" The only languages you may use are: {string.Join(", ", allowedNames)}. " +
                "If the user speaks or asks for another of these languages, switch to it. " +
                "If they ask for any other language, politely say you cannot use it and continue in the current one.";
        }

        return instruction;
    }

    private static string DisplayName(string tag)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag.Trim());
            var name = culture.EnglishName;

            return string.IsNullOrWhiteSpace(name) ? tag.Trim() : name;
        }
        catch (CultureNotFoundException)
        {
            return tag.Trim();
        }
    }
}
