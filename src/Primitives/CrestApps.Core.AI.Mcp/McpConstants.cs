namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Provides functionality for MCP Constants.
/// </summary>
public static class McpConstants
{
    public const string DataProtectionPurpose = "McpClientConnection";

    /// <summary>
    /// The name of the named <see cref="System.Net.Http.HttpClient"/> used by MCP services.
    /// </summary>
    public const string HttpClientName = "CrestApps.Mcp";

    /// <summary>
    /// Provides functionality for transport Types.
    /// </summary>
    public static class TransportTypes
    {
        public const string StdIo = "stdIo";

        public const string Sse = "sse";
    }
}
