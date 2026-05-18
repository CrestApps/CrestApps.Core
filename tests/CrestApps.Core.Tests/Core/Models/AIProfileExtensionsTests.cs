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

    [Fact]
    public void Serialize_WritesTypedSettingsInsideSettingsObject()
    {
        var profile = new AIProfile
        {
            Name = "profile-1",
        };

        profile.WithSettings(new TestSettings
        {
            Mode = TestMode.SecondValue,
        });

        var json = JsonSerializer.Serialize(profile);
        var node = JsonNode.Parse(json)?.AsObject();

        Assert.NotNull(node);
        Assert.Equal("profile-1", node[nameof(AIProfile.Name)]?.GetValue<string>());
        Assert.Null(node[nameof(TestSettings)]);
        Assert.Equal(
            "SecondValue",
            node[nameof(AIProfile.Settings)]?[nameof(TestSettings)]?[nameof(TestSettings.Mode)]?.GetValue<string>());
    }

    [Fact]
    public void Deserialize_ReadsTypedSettingsFromNestedSettingsObject()
    {
        const string json = """
            {
              "Name": "profile-1",
              "Settings": {
                "TestSettings": {
                  "Mode": "SecondValue"
                }
              }
            }
            """;

        var profile = JsonSerializer.Deserialize<AIProfile>(json);

        Assert.NotNull(profile);
        Assert.True(profile.TryGetSettings<TestSettings>(out var settings));
        Assert.NotNull(settings);
        Assert.Equal(TestMode.SecondValue, settings.Mode);
    }

    [Fact]
    public void Deserialize_IgnoresFlattenedSettingsOutsideNestedSettingsObject()
    {
        const string json = """
            {
              "Name": "profile-1",
              "TestSettings": {
                "Mode": "SecondValue"
              }
            }
            """;

        var profile = JsonSerializer.Deserialize<AIProfile>(json);

        Assert.NotNull(profile);
        Assert.False(profile.TryGetSettings<TestSettings>(out _));
    }

    [Fact]
    public void WithSettings_RoundTripsTypedSettingsOnlyThroughSettingsObject()
    {
        var profile = new AIProfile
        {
            Name = "profile-1",
        };

        profile.WithSettings(new TestSettings
        {
            Mode = TestMode.SecondValue,
        });

        var serializedJson = JsonSerializer.Serialize(profile);
        var serializedNode = JsonNode.Parse(serializedJson)?.AsObject();

        Assert.NotNull(serializedNode);
        Assert.Null(serializedNode[nameof(TestSettings)]);
        Assert.Equal("profile-1", serializedNode[nameof(AIProfile.Name)]?.GetValue<string>());

        var settingsNode = serializedNode[nameof(AIProfile.Settings)]?.AsObject();

        Assert.NotNull(settingsNode);
        Assert.Single(settingsNode);
        Assert.Equal(
            "SecondValue",
            settingsNode[nameof(TestSettings)]?[nameof(TestSettings.Mode)]?.GetValue<string>());
        Assert.Null(settingsNode[nameof(TestSettings.Mode)]);

        const string roundTripJson = """
            {
              "Name": "profile-1",
              "TestSettings": {
                "Mode": "FirstValue"
              },
              "Settings": {
                "TestSettings": {
                  "Mode": "SecondValue"
                }
              }
            }
            """;

        var roundTrippedProfile = JsonSerializer.Deserialize<AIProfile>(roundTripJson);

        Assert.NotNull(roundTrippedProfile);
        Assert.True(roundTrippedProfile.TryGetSettings<TestSettings>(out var settings));
        Assert.NotNull(settings);
        Assert.Equal(TestMode.SecondValue, settings.Mode);

        var property = Assert.Single(roundTrippedProfile.Settings);
        Assert.Equal(nameof(TestSettings), property.Key);
        Assert.False(roundTrippedProfile.Settings.ContainsKey(nameof(TestSettings.Mode)));
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
