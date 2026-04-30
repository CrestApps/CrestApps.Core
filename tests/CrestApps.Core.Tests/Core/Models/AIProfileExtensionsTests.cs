using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class AIProfileExtensionsTests
{
    [Fact]
    public void WithSettings_UsesProvidedSerializerOptions()
    {
        var profile = new AIProfile();
        var jsonSerializerOptions = CreateCamelCaseEnumOptions();

        profile.WithSettings(new TestSettings
        {
            Mode = TestMode.SecondValue,
        }, jsonSerializerOptions);

        var settingsNode = Assert.IsType<JsonObject>(profile.Settings[nameof(TestSettings)]);

        Assert.Equal("secondValue", settingsNode[nameof(TestSettings.Mode)]?.GetValue<string>());

        var settings = profile.GetSettings<TestSettings>(jsonSerializerOptions);

        Assert.Equal(TestMode.SecondValue, settings.Mode);
    }

    [Fact]
    public void AlterSettings_UsesProvidedSerializerOptions()
    {
        var jsonSerializerOptions = CreateCamelCaseEnumOptions();

        var profile = new AIProfile
        {
            Settings =
            {
                [nameof(TestSettings)] = JsonSerializer.SerializeToNode(new TestSettings
                {
                    Mode = TestMode.FirstValue,
                }, jsonSerializerOptions),
            },
        };

        profile.AlterSettings<TestSettings>(settings =>
        {
            settings.Mode = TestMode.SecondValue;
        }, jsonSerializerOptions);

        var settingsNode = Assert.IsType<JsonObject>(profile.Settings[nameof(TestSettings)]);

        Assert.Equal("secondValue", settingsNode[nameof(TestSettings.Mode)]?.GetValue<string>());
    }

    [Fact]
    public void GetSettings_FallsBackToFrameworkDefaultsWhenNoOptionsProvided()
    {
        var profile = new AIProfile();

        profile.WithSettings(new TestSettings
        {
            Mode = TestMode.SecondValue,
        });

        var settings = profile.GetSettings<TestSettings>();

        Assert.Equal(TestMode.SecondValue, settings.Mode);
    }

    private static JsonSerializerOptions CreateCamelCaseEnumOptions()
    {
        var options = new JsonSerializerOptions(ExtensibleEntityJsonOptions.CreateDefaultSerializerOptions());

        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    private sealed class TestSettings
    {
        public TestMode Mode { get; set; }
    }

    private enum TestMode
    {
        FirstValue,
        SecondValue,
    }
}
