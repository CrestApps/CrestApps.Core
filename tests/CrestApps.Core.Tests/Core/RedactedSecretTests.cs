using CrestApps.Core.Infrastructure;

namespace CrestApps.Core.Tests.Core;

public sealed class RedactedSecretTests
{
    [Fact]
    public void ToString_NeverReturnsRawValue()
    {
        var secret = new RedactedSecret("super-secret-token");

        var rendered = secret.ToString();

        Assert.Equal("***", rendered);
        Assert.DoesNotContain("super-secret-token", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Reveal_ReturnsOriginalValue()
    {
        var secret = new RedactedSecret("api-key-123");

        Assert.Equal("api-key-123", secret.Reveal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsEmpty_TrueForNullOrEmpty(string value)
    {
        var secret = new RedactedSecret(value);

        Assert.True(secret.IsEmpty);
        Assert.Equal("***", secret.ToString());
        Assert.Equal(value, secret.Reveal());
    }

    [Fact]
    public void IsEmpty_FalseForNonEmpty()
    {
        var secret = new RedactedSecret("x");

        Assert.False(secret.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateOrNull_ReturnsNullForNullOrEmpty(string value)
    {
        Assert.Null(RedactedSecret.CreateOrNull(value));
    }

    [Fact]
    public void CreateOrNull_ReturnsInstanceForNonEmpty()
    {
        var secret = RedactedSecret.CreateOrNull("token");

        Assert.NotNull(secret);
        Assert.Equal("token", secret.Reveal());
        Assert.Equal("***", secret.ToString());
    }

    [Fact]
    public void StringInterpolation_ProducesMaskedValue()
    {
        var secret = new RedactedSecret("leak-me");

        var line = $"key={secret}";

        Assert.Equal("key=***", line);
        Assert.DoesNotContain("leak-me", line, StringComparison.Ordinal);
    }
}
