using CrestApps.Core.AI.Copilot.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Copilot.Services;

internal sealed class CopilotOptionsValidator : IValidateOptions<CopilotOptions>
{
    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="options">The options.</param>
    public ValidateOptionsResult Validate(string name, CopilotOptions options)
    {
        if (options.AuthenticationType == CopilotAuthenticationType.GitHubOAuth)
        {
            if (string.IsNullOrWhiteSpace(options.ClientId))
            {
                return ValidateOptionsResult.Fail("CopilotOptions.ClientId is required when AuthenticationType is GitHubOAuth.");
            }

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                return ValidateOptionsResult.Fail("CopilotOptions.ClientSecret is required when AuthenticationType is GitHubOAuth.");
            }
        }

        if (options.AuthenticationType == CopilotAuthenticationType.ApiKey)
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                return ValidateOptionsResult.Fail("CopilotOptions.ApiKey is required when AuthenticationType is ApiKey.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
