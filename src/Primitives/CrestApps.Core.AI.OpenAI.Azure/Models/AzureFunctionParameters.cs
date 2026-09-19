namespace CrestApps.Core.AI.OpenAI.Azure.Models;

internal sealed class AzureFunctionParameters
{
    public string Type { get; set; } = "object";

    public Dictionary<string, AzureChatFunctionParameterArgument> Properties { get; set; }

    public IEnumerable<string> Required { get; set; }
}
