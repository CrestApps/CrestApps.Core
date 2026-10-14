using System.Text.Json;
using Microsoft.Extensions.AI;
using BlazorCalculatorTool = CrestApps.Core.Blazor.Web.Tools.CalculatorTool;
using MvcCalculatorTool = CrestApps.Core.Mvc.Web.Tools.CalculatorTool;

namespace CrestApps.Core.Tests.Framework.Startup;

public sealed class CalculatorToolTests
{
    /// <summary>
    /// Verifies that the MVC sample calculator reads an operation that arrives as a <see cref="JsonElement"/>,
    /// which is how the Azure OpenAI completion client passes tool-call arguments.
    /// </summary>
    [Fact]
    public async Task MvcCalculatorTool_InvokeAsync_ReadsOperationFromJsonElement()
    {
        await AssertMultipliesJsonElementArgumentsAsync(new MvcCalculatorTool());
    }


    /// <summary>
    /// Verifies that the Blazor sample calculator reads an operation that arrives as a <see cref="JsonElement"/>,
    /// which is how the Azure OpenAI completion client passes tool-call arguments.
    /// </summary>
    [Fact]
    public async Task BlazorCalculatorTool_InvokeAsync_ReadsOperationFromJsonElement()
    {
        await AssertMultipliesJsonElementArgumentsAsync(new BlazorCalculatorTool());
    }


    private static async Task AssertMultipliesJsonElementArgumentsAsync(AIFunction calculator)
    {
        var arguments = new AIFunctionArguments(JsonSerializer.Deserialize<Dictionary<string, object>>("""{"operation":"multiply","a":6,"b":7}"""));

        Assert.IsType<JsonElement>(arguments["operation"]);

        var result = await calculator.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(Assert.IsType<string>(result));

        Assert.False(document.RootElement.TryGetProperty("error", out _));
        Assert.Equal(42, document.RootElement.GetProperty("result").GetDouble());
    }
}
