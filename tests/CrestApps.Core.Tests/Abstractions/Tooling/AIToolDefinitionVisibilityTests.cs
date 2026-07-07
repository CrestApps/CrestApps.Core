using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.Tests.Abstractions.Tooling;

public sealed class AIToolDefinitionVisibilityTests
{
    [Fact]
    public void IsSelectable_ReturnsFalse_ForHiddenTool()
    {
        // Arrange
        var definition = new AIToolDefinitionEntry(typeof(TestTool))
        {
            Hidden = true,
        };

        // Act
        var result = definition.IsSelectable();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsSelectable_ReturnsFalse_ForSystemTool()
    {
        // Arrange
        var definition = new AIToolDefinitionEntry(typeof(TestTool))
        {
            IsSystemTool = true,
        };

        // Act
        var result = definition.IsSelectable();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetSelectableTools_ReturnsOnlySelectableTools()
    {
        // Arrange
        var options = new AIToolDefinitionOptions();
        var visible = new AIToolDefinitionEntry(typeof(TestTool));
        var hidden = new AIToolDefinitionEntry(typeof(TestTool)) { Hidden = true };
        var system = new AIToolDefinitionEntry(typeof(TestTool)) { IsSystemTool = true };

        options.SetTool("visible", visible);
        options.SetTool("hidden", hidden);
        options.SetTool("system", system);

        // Act
        var result = options.GetSelectableTools();

        // Assert
        Assert.Equal(["visible"], result.Keys);
    }

    private sealed class TestTool;
}
