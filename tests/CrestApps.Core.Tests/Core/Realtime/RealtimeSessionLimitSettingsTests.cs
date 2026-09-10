using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// Verifies the two guard rails that bound what a single realtime session can cost. Both exist to stop a session
/// running up an open, billed provider connection unnoticed, so what matters most here is that a host which
/// configures nothing still gets them: a default that resolved to "no limit" would be no guard rail at all.
/// </summary>
public sealed class RealtimeSessionLimitSettingsTests
{
    [Fact]
    public void GetIdleTimeout_WithNoConfiguration_UsesTheDefault()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(30), RealtimeTransportSettings.GetIdleTimeout(services));
    }

    [Fact]
    public void GetMaxSessionDuration_WithNoConfiguration_UsesTheDefault()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Equal(TimeSpan.FromMinutes(5), RealtimeTransportSettings.GetMaxSessionDuration(services));
    }

    [Fact]
    public void GetIdleTimeout_WithAConfiguredWindow_UsesIt()
    {
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.IdleTimeoutSeconds = 90)
            .BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(90), RealtimeTransportSettings.GetIdleTimeout(services));
    }

    [Fact]
    public void GetMaxSessionDuration_WithAConfiguredCap_UsesIt()
    {
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.MaxSessionDurationSeconds = 1800)
            .BuildServiceProvider();

        Assert.Equal(TimeSpan.FromMinutes(30), RealtimeTransportSettings.GetMaxSessionDuration(services));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetIdleTimeout_WhenSwitchedOff_ReturnsNull(int seconds)
    {
        // A host with its own session controls must be able to opt out, and the runner reads null as "run on".
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.IdleTimeoutSeconds = seconds)
            .BuildServiceProvider();

        Assert.Null(RealtimeTransportSettings.GetIdleTimeout(services));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetMaxSessionDuration_WhenSwitchedOff_ReturnsNull(int seconds)
    {
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.MaxSessionDurationSeconds = seconds)
            .BuildServiceProvider();

        Assert.Null(RealtimeTransportSettings.GetMaxSessionDuration(services));
    }
}
