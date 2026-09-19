using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.OpenAI.Azure.Models;

internal sealed class AzureChatFunctionParameterArgument
{
    public string Type { get; set; }

    public string Description { get; set; }

    public bool IsRequired { get; set; }

    public object DefaultValue { get; set; }

    [JsonPropertyName("enum")]
    public string[] Values { get; set; }
}
