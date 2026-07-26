using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Extension methods for <see cref="AIToolInstance"/>, including production of stable, model-safe
/// function names so that multiple instances built from the same source are exposed to the AI model as
/// distinct callable functions.
/// </summary>
public static class AIToolInstanceExtensions
{
    private const int MaxFunctionNameLength = 64;
    private const int HashSuffixLength = 8;

    /// <summary>
    /// The namespace prefix applied to every tool-instance function name. User-configured instances are
    /// named from arbitrary user input, whereas tools registered in code via <c>AddCoreAITool</c> are
    /// surfaced to the model under their bare registered name. Prefixing every instance function name
    /// guarantees a user-chosen instance name can never collide with a code-registered tool name in the
    /// single function namespace the AI model sees.
    /// </summary>
    public const string FunctionNamePrefix = "tool_instance_";

    /// <summary>
    /// Builds the unique function name presented to the AI model for the supplied instance. The name is
    /// derived from the instance's unique <see cref="AIToolInstance.Name"/> (falling back to its
    /// identifier), sanitized to the characters allowed by chat-completion providers (letters, digits,
    /// underscores, and hyphens), prefixed with <see cref="FunctionNamePrefix"/> so it can never collide
    /// with a code-registered tool name, and capped at 64 characters. When sanitizing or truncating would
    /// change the value, a short deterministic hash of the original unique name is appended so two distinct
    /// instance names can never collapse to the same function name.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <returns>A deterministic, provider-safe, collision-resistant function name.</returns>
    public static string GetFunctionName(this AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var original = !string.IsNullOrEmpty(instance.Name)
            ? instance.Name
            : instance.ItemId;

        var name = Sanitize(original);

        if (string.IsNullOrEmpty(name))
        {
            return FunctionNamePrefix + ComputeShortHash(original ?? string.Empty);
        }

        var maxBaseLength = MaxFunctionNameLength - FunctionNamePrefix.Length;
        var isLossy = !string.Equals(name, original, StringComparison.Ordinal);

        if (isLossy || name.Length > maxBaseLength)
        {
            var suffix = "_" + ComputeShortHash(original);
            var maxTruncatedLength = maxBaseLength - suffix.Length;

            if (name.Length > maxTruncatedLength)
            {
                name = name[..maxTruncatedLength];
            }

            name += suffix;
        }

        return FunctionNamePrefix + name;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            buffer[length++] = char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'
                ? c
                : '_';
        }

        return new string(buffer, 0, length);
    }

    private static string ComputeShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(hash)[..HashSuffixLength];
    }
}
