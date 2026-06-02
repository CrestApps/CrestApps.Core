namespace CrestApps.Core.AI.Security;

/// <summary>
/// Filters AI-generated output to prevent disclosure of sensitive system information,
/// user data leaks, and responses that indicate successful prompt injection.
/// </summary>
public interface IOutputSecurityFilter
{
    /// <summary>
    /// Validates AI-generated output for security concerns before it is delivered to the user.
    /// </summary>
    /// <param name="context">The output security context containing the response and metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="PromptSecurityResult"/> indicating whether the output is safe or should be blocked.
    /// </returns>
    Task<PromptSecurityResult> ValidateOutputAsync(OutputSecurityContext context, CancellationToken cancellationToken = default);
}
