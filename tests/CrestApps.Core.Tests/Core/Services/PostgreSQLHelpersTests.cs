using System.Text.RegularExpressions;
using CrestApps.Core.PostgreSQL;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class PostgreSQLHelpersTests
{
    /// <summary>
    /// Verifies table-name validation matches the legacy regular expression for ASCII inputs.
    /// </summary>
    [Fact]
    public void SanitizeTableName_ShouldMatchLegacyValidationForAsciiCharacters()
    {
        AssertValidationMatchesLegacy(
            PostgreSQLHelpers.SanitizeTableName,
            "^[A-Za-z0-9_-]+$");
    }

    /// <summary>
    /// Verifies identifier validation matches the legacy regular expression for ASCII inputs.
    /// </summary>
    [Fact]
    public void SanitizeIdentifier_ShouldMatchLegacyValidationForAsciiCharacters()
    {
        AssertValidationMatchesLegacy(
            PostgreSQLHelpers.SanitizeIdentifier,
            "^[A-Za-z0-9_-]+$");
    }

    /// <summary>
    /// Verifies column-name validation matches the legacy regular expression for ASCII inputs.
    /// </summary>
    [Fact]
    public void SanitizeColumnName_ShouldMatchLegacyValidationForAsciiCharacters()
    {
        AssertValidationMatchesLegacy(
            PostgreSQLHelpers.SanitizeColumnName,
            "^[A-Za-z0-9_]+$");
    }

    /// <summary>
    /// Verifies identifier normalization preserves legacy trimming, replacement, and casing.
    /// </summary>
    [Theory]
    [InlineData(" Customer-Orders ", "customer_orders")]
    [InlineData("already_normalized", "already_normalized")]
    [InlineData("ABC123", "abc123")]
    public void SanitizeIdentifier_ShouldPreserveLegacyNormalization(string value, string expected)
    {
        var result = PostgreSQLHelpers.SanitizeIdentifier(value);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies identifier quoting preserves legacy case and trimming behavior.
    /// </summary>
    [Theory]
    [InlineData(" Customer_Orders ", "Customer_Orders")]
    [InlineData("customer-orders", "customer-orders")]
    public void QuoteIdentifier_ShouldPreserveLegacyOutput(string value, string expected)
    {
        var result = PostgreSQLHelpers.QuoteIdentifier(value);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies invalid identifiers retain their exact legacy exception messages.
    /// </summary>
    [Fact]
    public void Sanitizers_ShouldPreserveLegacyExceptionMessages()
    {
        var tableException = Assert.Throws<InvalidOperationException>(
            () => PostgreSQLHelpers.SanitizeTableName(" invalid.name "));
        var identifierException = Assert.Throws<InvalidOperationException>(
            () => PostgreSQLHelpers.SanitizeIdentifier(" invalid.name "));
        var columnException = Assert.Throws<InvalidOperationException>(
            () => PostgreSQLHelpers.SanitizeColumnName(" invalid-name "));
        var quoteException = Assert.Throws<InvalidOperationException>(
            () => PostgreSQLHelpers.QuoteIdentifier(" invalid.name "));

        Assert.Equal(
            "The PostgreSQL table name 'invalid.name' contains unsupported characters.",
            tableException.Message);
        Assert.Equal(
            "The PostgreSQL identifier 'invalid.name' contains unsupported characters.",
            identifierException.Message);
        Assert.Equal(
            "The PostgreSQL column name 'invalid-name' contains unsupported characters.",
            columnException.Message);
        Assert.Equal(
            "The PostgreSQL identifier 'invalid.name' contains unsupported characters.",
            quoteException.Message);
    }

    /// <summary>
    /// Compares a sanitizer against the legacy regular-expression acceptance set.
    /// </summary>
    /// <param name="sanitize">The sanitizer under test.</param>
    /// <param name="pattern">The legacy validation pattern.</param>
    private static void AssertValidationMatchesLegacy(
        Func<string, string> sanitize,
        string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);

        for (var value = 0; value <= 127; value++)
        {
            var input = $"A{(char)value}z";

            if (regex.IsMatch(input))
            {
                _ = sanitize(input);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => sanitize(input));
            }
        }
    }
}
