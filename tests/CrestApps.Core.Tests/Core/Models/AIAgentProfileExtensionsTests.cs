using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Models;

public class AIAgentProfileExtensionsTests
{
    [Fact]
    public void IsAlwaysAvailableAgent_ReturnsTrue_WhenAlwaysAvailable()
    {
        var profile = new AIProfile { Name = "a", Description = "d", Type = AIProfileType.Agent };
        profile.Put(new AgentMetadata { Availability = AgentAvailability.AlwaysAvailable });

        Assert.True(profile.IsAlwaysAvailableAgent());
    }

    [Fact]
    public void IsAlwaysAvailableAgent_ReturnsFalse_WhenOnDemand()
    {
        var profile = new AIProfile { Name = "a", Description = "d", Type = AIProfileType.Agent };
        profile.Put(new AgentMetadata { Availability = AgentAvailability.OnDemand });

        Assert.False(profile.IsAlwaysAvailableAgent());
    }

    [Fact]
    public void IsUserSelectableAgent_ReturnsFalse_WhenAlwaysAvailable()
    {
        var profile = new AIProfile { Name = "a", Description = "d", Type = AIProfileType.Agent };
        profile.Put(new AgentMetadata { Availability = AgentAvailability.AlwaysAvailable });

        Assert.False(profile.IsUserSelectableAgent());
    }

    [Fact]
    public void IsUserSelectableAgent_ReturnsTrue_WhenOnDemandWithDescription()
    {
        var profile = new AIProfile { Name = "a", Description = "d", Type = AIProfileType.Agent };
        profile.Put(new AgentMetadata { Availability = AgentAvailability.OnDemand });

        Assert.True(profile.IsUserSelectableAgent());
    }

    [Fact]
    public void IsUserSelectableAgent_ReturnsFalse_WhenNoDescription()
    {
        var profile = new AIProfile { Name = "a", Type = AIProfileType.Agent };

        Assert.False(profile.IsUserSelectableAgent());
    }

    [Fact]
    public void IsSystemAgent_ReturnsTrue_WhenSystem()
    {
        var profile = new AIProfile { Name = "a", Description = "d", Type = AIProfileType.Agent };
        profile.Put(new AgentMetadata { IsSystem = true });

        Assert.True(profile.IsSystemAgent());
    }
}
