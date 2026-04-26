using CrestApps.Core.AI.Claude.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Claude.Services;

internal sealed class ClaudeOptionsValidator : IValidateOptions<ClaudeOptions>
{
    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="options">The options.</param>
    public ValidateOptionsResult Validate(string name, ClaudeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail("ClaudeOptions.ApiKey is required. Configure it in your appsettings under the Claude section.");
        }

        return ValidateOptionsResult.Success;
    }
}
