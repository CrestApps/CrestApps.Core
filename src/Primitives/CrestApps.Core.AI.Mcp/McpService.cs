using System.Diagnostics;
using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Represents the MCP Service.
/// </summary>
public sealed class McpService
{
    private readonly IEnumerable<IMcpClientTransportProvider> _providers;
    private readonly ILogger<McpService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpService"/> class.
    /// </summary>
    /// <param name="providers">The providers.</param>
    /// <param name="logger">The logger.</param>
    public McpService(
        IEnumerable<IMcpClientTransportProvider> providers,
        ILogger<McpService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Gets or create client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public async Task<McpClient> GetOrCreateClientAsync(McpConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IClientTransport transport = null;

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(connection))
            {
                continue;
            }

            transport = await provider.GetAsync(connection);
        }

        if (transport is null)
        {
            _logger.LogWarning("Unable to find an implementation of '{TypeName}' that supports the connection. Not supported transport type.", nameof(IMcpClientTransportProvider));

            return null;
        }

        // Every call connects and runs the MCP handshake again, so the rate of this metric is also the rate of
        // new MCP clients, which makes a leak or a missing cache visible.
        using var activity = CrestAppsTelemetry.ActivitySource.StartActivity("crestapps.mcp.connect", ActivityKind.Client);
        activity?.SetTag(AITelemetry.TransportTag, connection.Source);

        var transportTag = new KeyValuePair<string, object>(AITelemetry.TransportTag, connection.Source);
        var startTimestamp = Stopwatch.GetTimestamp();
        var outcome = CrestAppsTelemetry.OutcomeOk;

        try
        {
            var client = await McpClient.CreateAsync(transport);

            // The session stays open until the client is disposed or its transport closes. A caller that never
            // disposes its client shows here as a count that only grows.
            AITelemetry.McpActiveClients.Add(1, transportTag);
            _ = client.Completion.ContinueWith(_ => AITelemetry.McpActiveClients.Add(-1, transportTag), TaskScheduler.Default);

            return client;
        }
        catch (Exception ex)
        {
            outcome = CrestAppsTelemetry.GetOutcome(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);

            throw;
        }
        finally
        {
            AITelemetry.McpClientConnectDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                transportTag,
                new KeyValuePair<string, object>(CrestAppsTelemetry.OutcomeTag, outcome));
        }
    }
}
