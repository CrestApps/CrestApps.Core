namespace CrestApps.Core.AI.Security;

/// <summary>
/// Validates user prompts for security threats including prompt injection,
/// role impersonation, and data extraction attempts before they reach the AI model.
/// </summary>
public interface IPromptSecurityService
{
    /// <summary>
    /// Validates a user prompt for potential security threats.
    /// </summary>
    /// <param name="context">The prompt security context containing the input and metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="PromptSecurityResult"/> indicating whether the prompt is safe,
    /// flagged, or blocked.
    /// </returns>
    Task<PromptSecurityResult> ValidateInputAsync(PromptSecurityContext context, CancellationToken cancellationToken = default);
}
