using System.Text.Json;
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

    private static JsonSerializerOptions CreateCamelCaseEnumOptions()
    {
        var options = new JsonSerializerOptions(ExtensibleEntityJsonOptions.CreateDefaultSerializerOptions());

        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    private sealed class TestExtensibleEntity : ExtensibleEntity
    {
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
