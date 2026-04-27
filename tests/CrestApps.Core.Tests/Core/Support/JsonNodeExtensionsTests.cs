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
}
