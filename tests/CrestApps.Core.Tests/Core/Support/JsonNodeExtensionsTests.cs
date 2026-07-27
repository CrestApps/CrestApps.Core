using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;

namespace CrestApps.Core.Tests.Core.Support;

public sealed class JsonNodeExtensionsTests
{
    [Fact]
    public void TryGetTrimmedStringValue_ReturnsTrimmedValue()
    {
        JsonObject json = new()
        {
            ["Name"] = "  test  ",
        };

        var found = json.TryGetTrimmedStringValue("Name", out var value);

        Assert.True(found);
        Assert.Equal("test", value);
    }

    [Fact]
    public void TryGetEnumValue_ParsesStringValues()
    {
        JsonObject json = new()
        {
            ["Mode"] = "Conversation",
        };

        var found = json.TryGetEnumValue("Mode", out ChatMode value);

        Assert.True(found);
        Assert.Equal(ChatMode.Conversation, value);
    }

    [Fact]
    public void TryGetDictionaryValue_ParsesJsonTextValues()
    {
        JsonObject json = new()
        {
            ["Headers"] = "{\"x-api-key\":\"secret\"}",
        };

        var found = json.TryGetDictionaryValue("Headers", out var values);

        Assert.True(found);
        Assert.Equal("secret", values["x-api-key"]);
    }

    [Fact]
    public void GetRawValue_NullNode_ReturnsNull()
    {
        JsonNode node = null;

        var value = node.GetRawValue();

        Assert.Null(value);
    }

    [Fact]
    public void GetRawValue_JsonObject_ReturnsCaseInsensitiveDictionaryInPropertyOrder()
    {
        JsonObject json = new()
        {
            ["third"] = 3L,
            ["first"] = 1L,
            ["second"] = 2L,
        };

        var values = Assert.IsType<Dictionary<string, object>>(json.GetRawValue());

        Assert.Same(StringComparer.OrdinalIgnoreCase, values.Comparer);
        Assert.Equal(["third", "first", "second"], values.Keys);
        Assert.Equal(1L, values["FIRST"]);
    }

    [Fact]
    public void GetRawValue_JsonArray_ReturnsListOfObjects()
    {
        var json = JsonNode.Parse("""[1,"two",true,null]""");

        var values = Assert.IsType<List<object>>(json.GetRawValue());

        Assert.Equal(4, values.Count);
        Assert.Equal(1L, Assert.IsType<long>(values[0]));
        Assert.Equal("two", Assert.IsType<string>(values[1]));
        Assert.True(Assert.IsType<bool>(values[2]));
        Assert.Null(values[3]);
    }

    [Fact]
    public void GetRawValue_NestedObjectsAndArrays_PreserveConcreteContainerTypesAndNulls()
    {
        var json = JsonNode.Parse(
            """
            {
              "name": "catalog",
              "metadata": {
                "count": 7,
                "none": null
              },
              "items": [
                {
                  "enabled": true,
                  "tags": ["one", null, "three"]
                },
                null
              ]
            }
            """);

        var root = Assert.IsType<Dictionary<string, object>>(json.GetRawValue());
        var metadata = Assert.IsType<Dictionary<string, object>>(root["metadata"]);
        var items = Assert.IsType<List<object>>(root["items"]);
        var firstItem = Assert.IsType<Dictionary<string, object>>(items[0]);
        var tags = Assert.IsType<List<object>>(firstItem["tags"]);

        Assert.Equal("catalog", root["name"]);
        Assert.Equal(7L, metadata["count"]);
        Assert.Null(metadata["none"]);
        Assert.True(Assert.IsType<bool>(firstItem["enabled"]));
        Assert.Equal(["one", null, "three"], tags);
        Assert.Null(items[1]);
    }

    [Fact]
    public void GetRawValue_StringNodes_ReturnPlainStrings()
    {
        var createdValue = JsonValue.Create("created");
        var parsedValue = JsonNode.Parse("\"parsed\"");

        Assert.Equal("created", Assert.IsType<string>(createdValue.GetRawValue()));
        Assert.Equal("parsed", Assert.IsType<string>(parsedValue.GetRawValue()));
    }

    [Fact]
    public void GetRawValue_LongNodes_PreserveCurrentTryGetValueSemantics()
    {
        var longValue = JsonValue.Create(42L);
        var parsedInteger = JsonNode.Parse("42");
        var intValue = JsonValue.Create(42);

        Assert.Equal(42L, Assert.IsType<long>(longValue.GetRawValue()));
        Assert.Equal(42L, Assert.IsType<long>(parsedInteger.GetRawValue()));
        Assert.Equal("42", Assert.IsType<string>(intValue.GetRawValue()));
    }

    [Fact]
    public void GetRawValue_FloatingPointNodes_PreserveCurrentTryGetValueSemantics()
    {
        var doubleValue = JsonValue.Create(1.25d);
        var decimalValue = JsonValue.Create(1.25m);
        var floatValue = JsonValue.Create(1.25f);
        var parsedNumber = JsonNode.Parse("1.25");

        Assert.Equal(1.25d, Assert.IsType<double>(doubleValue.GetRawValue()));
        Assert.Equal("1.25", Assert.IsType<string>(decimalValue.GetRawValue()));
        Assert.Equal("1.25", Assert.IsType<string>(floatValue.GetRawValue()));
        Assert.Equal(1.25d, Assert.IsType<double>(parsedNumber.GetRawValue()));
    }

    [Fact]
    public void GetRawValue_BooleanNodes_ReturnBooleans()
    {
        var createdValue = JsonValue.Create(true);
        var parsedValue = JsonNode.Parse("false");

        Assert.True(Assert.IsType<bool>(createdValue.GetRawValue()));
        Assert.False(Assert.IsType<bool>(parsedValue.GetRawValue()));
    }

    [Fact]
    public void GetRawValue_DateNodes_PreserveCurrentTryGetValueSemantics()
    {
        var dateTime = new DateTime(2025, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
        var dateTimeOffset = new DateTimeOffset(2025, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(-7));
        var createdDateTime = JsonValue.Create(dateTime);
        var createdDateTimeOffset = JsonValue.Create(dateTimeOffset);
        var parsedDateTimeText = JsonNode.Parse("\"2025-01-02T03:04:05.678Z\"");

        Assert.Equal(dateTime, Assert.IsType<DateTime>(createdDateTime.GetRawValue()));
        Assert.Equal(
            "\"2025-01-02T03:04:05.678-07:00\"",
            Assert.IsType<string>(createdDateTimeOffset.GetRawValue()));
        Assert.Equal(
            "2025-01-02T03:04:05.678Z",
            Assert.IsType<string>(parsedDateTimeText.GetRawValue()));
    }

    [Fact]
    public void GetRawValue_JsonElementBackedValues_UsePrimitiveConversions()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "integer": 42,
              "floating": 1.25,
              "string": "value",
              "boolean": true
            }
            """);
        var root = document.RootElement;

        var integerValue = JsonValue.Create(root.GetProperty("integer").Clone());
        var floatingValue = JsonValue.Create(root.GetProperty("floating").Clone());
        var stringValue = JsonValue.Create(root.GetProperty("string").Clone());
        var booleanValue = JsonValue.Create(root.GetProperty("boolean").Clone());

        Assert.Equal(42L, Assert.IsType<long>(integerValue.GetRawValue()));
        Assert.Equal(1.25d, Assert.IsType<double>(floatingValue.GetRawValue()));
        Assert.Equal("value", Assert.IsType<string>(stringValue.GetRawValue()));
        Assert.True(Assert.IsType<bool>(booleanValue.GetRawValue()));
    }

    [Fact]
    public void GetRawValue_CustomValue_ReturnsExactToJsonStringOutput()
    {
        var json = JsonValue.Create(new CustomValue("Ada", 42));

        var value = Assert.IsType<string>(json.GetRawValue());

        Assert.Equal("""{"Name":"Ada","Count":42}""", value);
    }

    [Fact]
    public void GetRawValue_DuplicateKeysDifferingOnlyByCase_ThrowsRatherThanOverwriting()
    {
        JsonObject json = new()
        {
            ["Name"] = "first",
            ["name"] = "second",
        };

        Assert.Throws<ArgumentException>(() => json.GetRawValue());
    }

    [Fact]
    public void GetRawValue_DeeplyNestedNodes_PreserveRecursiveShape()
    {
        const int depth = 256;

        JsonNode json = JsonValue.Create("leaf");

        for (var index = 0; index < depth; index++)
        {
            json = index % 2 == 0
                ? new JsonObject
                {
                    ["child"] = json,
                }
                : new JsonArray(json);
        }

        object current = json.GetRawValue();

        for (var index = depth - 1; index >= 0; index--)
        {
            current = index % 2 == 0
                ? Assert.IsType<Dictionary<string, object>>(current)["child"]
                : Assert.IsType<List<object>>(current)[0];
        }

        Assert.Equal("leaf", current);
    }

    [Fact]
    public void GetRawValue_ReturnedContainersAreIndependentFromSourceNodes()
    {
        var json = JsonNode.Parse(
            """
            {
              "metadata": {
                "name": "original"
              },
              "items": ["first"]
            }
            """).AsObject();
        var values = Assert.IsType<Dictionary<string, object>>(json.GetRawValue());
        var metadata = Assert.IsType<Dictionary<string, object>>(values["metadata"]);
        var items = Assert.IsType<List<object>>(values["items"]);

        metadata["name"] = "returned";
        items.Add("returned");

        Assert.Equal("original", json["metadata"]["name"].GetValue<string>());
        Assert.Single(json["items"].AsArray());

        json["metadata"]["name"] = "source";
        json["items"].AsArray().Add("source");

        Assert.Equal("returned", metadata["name"]);
        Assert.Equal(["first", "returned"], items);
    }

    private sealed record CustomValue(string Name, int Count);
}
