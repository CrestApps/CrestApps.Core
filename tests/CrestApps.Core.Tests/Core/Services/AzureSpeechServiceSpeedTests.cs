#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using System.Xml.Linq;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Azure Speech is given a speaking speed as SSML <c>prosody rate</c>, so a cascade that speaks with it honours its
/// configured pace and the listener's speed control (CrestApps.Core#273).
/// </summary>
public sealed class AzureSpeechServiceSpeedTests
{
    private static readonly XNamespace _ssml = "http://www.w3.org/2001/10/synthesis";

    [Theory]
    [InlineData(1.2f, "+20%")]
    [InlineData(0.75f, "-25%")]
    [InlineData(1.125f, "+12.5%")]
    [InlineData(3f, "+100%")]
    [InlineData(0.1f, "-50%")]
    public void BuildSsml_GivesTheSpeedAsAProsodyRate_WithinWhatAzureAccepts(float speed, string expectedRate)
    {
        var ssml = AzureSpeechServiceTextToSpeechClient.BuildSsml("Hello.", new TextToSpeechOptions { VoiceId = "en-US-JennyNeural", Speed = speed });

        var prosody = XDocument.Parse(ssml).Descendants(_ssml + "prosody").Single();
        Assert.Equal(expectedRate, (string)prosody.Attribute("rate"));
        Assert.Equal("Hello.", prosody.Value);
    }

    [Fact]
    public void BuildSsml_NamesTheVoiceInItsOwnLanguage_AndEscapesTheText()
    {
        var ssml = AzureSpeechServiceTextToSpeechClient.BuildSsml(
            "Tom & Jerry <arrive> at \"nine\".",
            new TextToSpeechOptions { VoiceId = "el-GR-AthinaNeural", Language = "en", Speed = 1.2f });

        var document = XDocument.Parse(ssml);
        Assert.Equal("el-GR", (string)document.Root.Attribute(XNamespace.Xml + "lang"));
        Assert.Equal("el-GR-AthinaNeural", (string)document.Descendants(_ssml + "voice").Single().Attribute("name"));
        Assert.Equal("Tom & Jerry <arrive> at \"nine\".", document.Descendants(_ssml + "prosody").Single().Value);
    }

    [Theory]
    [InlineData(null, "en-US-JennyNeural")]
    [InlineData(1f, "en-US-JennyNeural")]
    [InlineData(1.2f, null)]
    [InlineData(1.2f, "")]
    [InlineData(float.NaN, "en-US-JennyNeural")]
    public void BuildSsml_WithoutASpeedToApplyOrAVoiceToName_SpeaksThePlainText(float? speed, string voice)
    {
        Assert.Null(AzureSpeechServiceTextToSpeechClient.BuildSsml("Hello.", new TextToSpeechOptions { VoiceId = voice, Speed = speed }));
    }
}
