using CrestApps.Core.AI.Claude.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Claude.Services;

internal sealed class ClaudeOptionsValidator : IValidateOptions<ClaudeOptions>
{
    public ValidateOptionsResult Validate(string name, ClaudeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail("ClaudeOptions.ApiKey is required. Configure it in your appsettings under the Claude section.");
        }

        return ValidateOptionsResult.Success;
    }
}
