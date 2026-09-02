#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using System.Text.Json;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class DefaultRealtimeSessionConfiguratorTests
{
    [Fact]
    public void Configure_MapsInstructionsVoiceModelAndAudioDefaults()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            Instructions = "You are a helpful assistant with knowledge base access.",
            Voice = "cedar",
            MaxOutputTokens = 512,
            SpeechLanguage = "en",
        });

        Assert.Equal("gpt-realtime", options.Model);
        // When a speech language is set, the configurator prepends a directive that locks the reply language
        // (realtime models otherwise drift), followed by the profile instructions.
        Assert.Equal(
            "Always speak and respond in English. Do not switch to another language on your own — " +
            "only use a different language if the user explicitly asks you to speak that language.\n\n" +
            "You are a helpful assistant with knowledge base access.",
            options.Instructions);
        Assert.Equal("cedar", options.Voice);
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(["audio"], options.OutputModalities);
        Assert.Equal(24000, options.InputAudioFormat?.SampleRate);
        Assert.Equal(24000, options.OutputAudioFormat?.SampleRate);
        Assert.True(options.VoiceActivityDetection?.Enabled);
        Assert.True(options.VoiceActivityDetection?.AllowInterruption);
        Assert.Equal("whisper-1", options.TranscriptionOptions?.ModelId);
        Assert.Equal("en", options.TranscriptionOptions?.SpeechLanguage);
    }

    [Fact]
    public void Configure_WithTools_SetsToolsAndAutoToolMode()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            Tools = [new StubTool("search_data_sources"), new StubTool("current_date_time")],
        });

        Assert.NotNull(options.Tools);
        Assert.Equal(2, options.Tools!.Count);
        Assert.IsType<AutoChatToolMode>(options.ToolMode);
    }

    [Fact]
    public void Configure_WithoutTools_LeavesToolsAndToolModeNull()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
        });

        Assert.Null(options.Tools);
        Assert.Null(options.ToolMode);
    }

    [Fact]
    public void Configure_WithoutInterruption_DisablesBargeIn()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            AllowInterruption = false,
        });

        Assert.False(options.VoiceActivityDetection?.AllowInterruption);
    }

    [Fact]
    public void Configure_WithoutTranscriptionModel_LeavesTranscriptionNull()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            InputTranscriptionModel = null,
        });

        Assert.Null(options.TranscriptionOptions);
    }

    private sealed class StubTool : AIFunction
    {
        public StubTool(string name)
        {
            Name = name;
        }

        public override string Name { get; }

        public override string Description => Name;

        public override JsonElement JsonSchema => JsonSerializer.Deserialize<JsonElement>("{}");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => new(Name);
    }
}
