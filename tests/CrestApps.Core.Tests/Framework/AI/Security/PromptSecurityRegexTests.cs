using System.Reflection;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Framework.AI.Security;

/// <summary>
/// Verifies the match timeout of the built-in prompt and output security patterns.
/// </summary>
public sealed class PromptSecurityRegexTests
{
    /// <summary>
    /// Verifies every built-in rule and output filter pattern allows the shared timeout, so none of them fails
    /// a safe input on a busy host with a budget shorter than its first match can take.
    /// </summary>
    [Fact]
    public void BuiltInSecurityPatterns_UseTheSharedMatchTimeout()
    {
        var securityTypes = typeof(DefaultOutputSecurityFilter).Assembly
            .GetTypes()
            .Where(type => type == typeof(DefaultOutputSecurityFilter) || (!type.IsAbstract && typeof(RegexPromptSecurityRuleBase).IsAssignableFrom(type)))
            .ToArray();

        Assert.Contains(typeof(EncodedExfiltrationRule), securityTypes);
        Assert.Contains(typeof(HiddenContextDisclosureRule), securityTypes);

        foreach (var securityType in securityTypes)
        {
            var patterns = securityType
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(method => method.ReturnType == typeof(Regex) && method.GetParameters().Length == 0)
                .Select(method => (Regex)method.Invoke(null, null))
                .ToArray();

            Assert.NotEmpty(patterns);
            Assert.All(patterns, pattern => Assert.Equal(TimeSpan.FromMilliseconds(PromptSecurityRegex.MatchTimeoutMilliseconds), pattern.MatchTimeout));
        }
    }
}
