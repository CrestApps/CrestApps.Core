using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;

namespace CrestApps.Core.Tests.Core.Speech;

public sealed class ConversationLanguageTests
{
    private static AIProfile ProfileWith(Action<AIProfileLanguageSettings> configure)
    {
        return new AIProfile().AlterSettings(configure);
    }

    [Fact]
    public void ResolveStartingLanguage_PrefersAnAllowedConversationVariable()
    {
        var service = new DefaultConversationLanguageService([]);
        var profile = ProfileWith(s =>
        {
            s.AllowedLanguages = ["en-US", "es"];
            s.StartingLanguage = "en-US";
        });

        Assert.Equal("es", service.ResolveStartingLanguage(profile, "es-MX"));
        Assert.Equal("en-US", service.ResolveStartingLanguage(profile, "de-DE"));
        Assert.Equal("en-US", service.ResolveStartingLanguage(profile));
    }

    [Fact]
    public void ResolveStartingLanguage_WithoutSettings_ReturnsTheVariableOrNull()
    {
        var service = new DefaultConversationLanguageService([]);

        Assert.Null(service.ResolveStartingLanguage(new AIProfile()));
        Assert.Equal("fr-FR", service.ResolveStartingLanguage(new AIProfile(), "fr-FR"));
    }

    [Fact]
    public async Task ReportDetectedLanguage_NotifiesHandlersOnlyForAnAllowedNewLanguage()
    {
        var handler = new RecordingHandler { VoiceToSet = "voz" };
        var service = new DefaultConversationLanguageService([handler]);
        var profile = ProfileWith(s => s.AllowedLanguages = ["en-US", "es"]);

        Assert.Null(await service.ReportDetectedLanguageAsync(profile, "s1", "en-US", "de-DE", TestContext.Current.CancellationToken));
        Assert.Null(await service.ReportDetectedLanguageAsync(profile, "s1", "en-US", "en-GB", TestContext.Current.CancellationToken));
        Assert.Empty(handler.Seen);

        var context = await service.ReportDetectedLanguageAsync(profile, "s1", "en-US", "es-MX", TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Equal("es", context.Language);
        Assert.Equal("en-US", context.PreviousLanguage);
        Assert.Equal("voz", context.VoiceName);
        Assert.Single(handler.Seen);
    }

    [Fact]
    public async Task Handler_AddsTheReplyLanguageInstruction()
    {
        var profile = ProfileWith(s =>
        {
            s.AllowedLanguages = ["en-US", "es"];
            s.StartingLanguage = "en-US";
        });

        var completion = new AICompletionContext { SystemMessage = "Be helpful." };
        completion.AdditionalProperties[AICompletionContextKeys.ConversationLanguage] = "es-MX";

        await new AIProfileLanguageCompletionContextBuilderHandler().BuiltAsync(new AICompletionContextBuiltContext(profile, completion));

        Assert.StartsWith("Be helpful.", completion.SystemMessage);
        Assert.Contains("Reply in Spanish", completion.SystemMessage);
        Assert.Contains("English", completion.SystemMessage);
        Assert.Equal("es", completion.AdditionalProperties[AICompletionContextKeys.ConversationLanguage]);
    }

    [Fact]
    public async Task Handler_FallsBackToTheStartingLanguage()
    {
        var profile = ProfileWith(s => s.StartingLanguage = "fr-FR");
        var completion = new AICompletionContext { SystemMessage = "Hi." };

        await new AIProfileLanguageCompletionContextBuilderHandler().BuiltAsync(new AICompletionContextBuiltContext(profile, completion));

        Assert.Contains("Reply in French", completion.SystemMessage);
    }

    [Fact]
    public async Task Handler_LeavesProfilesWithoutLanguageSettingsAlone()
    {
        var completion = new AICompletionContext { SystemMessage = "Hi." };

        await new AIProfileLanguageCompletionContextBuilderHandler().BuiltAsync(new AICompletionContextBuiltContext(new AIProfile(), completion));

        Assert.Equal("Hi.", completion.SystemMessage);
    }

    private sealed class RecordingHandler : IConversationLanguageHandler
    {
        public string VoiceToSet { get; set; }

        public List<DetectedLanguageContext> Seen { get; } = [];

        public Task LanguageDetectedAsync(DetectedLanguageContext context)
        {
            context.VoiceName = VoiceToSet;
            Seen.Add(context);

            return Task.CompletedTask;
        }
    }
}
