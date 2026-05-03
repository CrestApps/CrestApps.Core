using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides functionality for AI Configuration Record I Ds.
/// </summary>
public static class AIConfigurationRecordIds
{
    private const string _connectionPrefix = "cfgc";
    private const string _deploymentPrefix = "cfgd";

    /// <summary>
    /// Creates connection id.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="connectionName">The connection name.</param>
    public static string CreateConnectionId(string providerName, string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        var input = $"{providerName}:{connectionName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));

        return $"{_connectionPrefix}{Convert.ToHexStringLower(hash)[..22]}";
    }

    /// <summary>
    /// Creates deployment id.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="connectionName">The connection name.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public static string CreateDeploymentId(string providerName, string connectionName, string deploymentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);
        var input = $"{providerName}:{connectionName ?? string.Empty}:{deploymentName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));

        return $"{_deploymentPrefix}{Convert.ToHexStringLower(hash)[..22]}";
    }
}
