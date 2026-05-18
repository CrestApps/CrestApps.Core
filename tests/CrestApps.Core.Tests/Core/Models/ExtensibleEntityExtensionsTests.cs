using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class ExtensibleEntityExtensionsTests
{
    [Fact]
    public void Get_UsesProvidedSerializerOptions()
    {
        var jsonSerializerOptions = CreateCamelCaseEnumOptions();
        var entity = new TestExtensibleEntity();

        entity.Properties[nameof(TestSettings)] = JsonSerializer.SerializeToElement(new TestSettings
        {
            Mode = TestMode.SecondValue,
        }, jsonSerializerOptions);

        var settings = entity.Get<TestSettings>(nameof(TestSettings), jsonSerializerOptions);

        Assert.NotNull(settings);
        Assert.Equal(TestMode.SecondValue, settings.Mode);
    }

    [Fact]
    public void Serialize_WritesTypedMetadataInsidePropertiesObject()
    {
        var entity = new TestExtensibleEntity
        {
            Id = "entity-1",
        };

        entity.Put(new TestSettings
        {
            Mode = TestMode.SecondValue,
        });

        var json = JsonSerializer.Serialize(entity);
        var node = JsonNode.Parse(json)?.AsObject();

        Assert.NotNull(node);
        Assert.Equal("entity-1", node[nameof(TestExtensibleEntity.Id)]?.GetValue<string>());
        Assert.Null(node[nameof(TestSettings)]);
        Assert.Equal(
            "SecondValue",
            node[nameof(ExtensibleEntity.Properties)]?[nameof(TestSettings)]?[nameof(TestSettings.Mode)]?.GetValue<string>());
    }

    [Fact]
    public void Deserialize_ReadsTypedMetadataFromNestedPropertiesObject()
    {
        const string json = """
            {
              "Id": "entity-1",
              "Properties": {
                "TestSettings": {
                  "Mode": "SecondValue"
                }
              }
            }
            """;

        var entity = JsonSerializer.Deserialize<TestExtensibleEntity>(json);

        Assert.NotNull(entity);
        Assert.True(entity.TryGet<TestSettings>(out var settings));
        Assert.NotNull(settings);
        Assert.Equal(TestMode.SecondValue, settings.Mode);
    }

    [Fact]
    public void Deserialize_IgnoresFlattenedPropertiesOutsideNestedPropertiesObject()
    {
        const string json = """
            {
              "Id": "entity-1",
              "TestSettings": {
                "Mode": "SecondValue"
              }
            }
            """;

        var entity = JsonSerializer.Deserialize<TestExtensibleEntity>(json);

        Assert.NotNull(entity);
        Assert.False(entity.TryGet<TestSettings>(out _));
    }

    [Fact]
    public void Put_RoundTripsTypedMetadataOnlyThroughPropertiesObject()
    {
        var entity = new TestExtensibleEntity
        {
            Id = "entity-1",
        };

        entity.Put(new TestSettings
        {
            Mode = TestMode.SecondValue,
        });

        var serializedJson = JsonSerializer.Serialize(entity);
        var serializedNode = JsonNode.Parse(serializedJson)?.AsObject();

        Assert.NotNull(serializedNode);
        Assert.Null(serializedNode[nameof(TestSettings)]);
        Assert.Equal("entity-1", serializedNode[nameof(TestExtensibleEntity.Id)]?.GetValue<string>());

        var propertiesNode = serializedNode[nameof(ExtensibleEntity.Properties)]?.AsObject();

        Assert.NotNull(propertiesNode);
        Assert.Single(propertiesNode);
        Assert.Equal(
            "SecondValue",
            propertiesNode[nameof(TestSettings)]?[nameof(TestSettings.Mode)]?.GetValue<string>());
        Assert.Null(propertiesNode[nameof(TestSettings.Mode)]);

        const string roundTripJson = """
            {
              "Id": "entity-1",
              "TestSettings": {
                "Mode": "FirstValue"
              },
              "Properties": {
                "TestSettings": {
                  "Mode": "SecondValue"
                }
              }
            }
            """;

        var roundTrippedEntity = JsonSerializer.Deserialize<TestExtensibleEntity>(roundTripJson);

        Assert.NotNull(roundTrippedEntity);
        Assert.True(roundTrippedEntity.TryGet<TestSettings>(out var settings));
        Assert.NotNull(settings);
        Assert.Equal(TestMode.SecondValue, settings.Mode);

        var property = Assert.Single(roundTrippedEntity.Properties);
        Assert.Equal(nameof(TestSettings), property.Key);
        Assert.False(roundTrippedEntity.Properties.ContainsKey(nameof(ExtensibleEntity.Properties)));
        Assert.False(roundTrippedEntity.Properties.ContainsKey(nameof(TestSettings.Mode)));
    }

    private static JsonSerializerOptions CreateCamelCaseEnumOptions()
    {
        var options = new JsonSerializerOptions(ExtensibleEntityJsonOptions.CreateDefaultSerializerOptions());

        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    private sealed class TestExtensibleEntity : ExtensibleEntity
    {
        public string Id { get; set; }
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
