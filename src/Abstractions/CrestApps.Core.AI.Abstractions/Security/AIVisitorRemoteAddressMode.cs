namespace CrestApps.Core.AI.Security;

/// <summary>
/// Determines how the framework captures remote-address data for AI chat requests.
/// </summary>
public enum AIVisitorRemoteAddressMode
{
    /// <summary>
    /// Do not capture or persist remote-address data.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Capture only a salted hash of the remote address for privacy-first abuse controls.
    /// </summary>
    Hashed = 1,

    /// <summary>
    /// Capture the remote address in plain text for operational controls such as blocklists.
    /// </summary>
    PlainText = 2,

    /// <summary>
    /// Capture the remote address in encrypted form for at-rest protection while still allowing hashed abuse partitioning.
    /// </summary>
    Encrypted = 3,
}
