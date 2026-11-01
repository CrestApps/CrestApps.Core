namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// What a page tool answered in the browser tab.
/// </summary>
public sealed class WebMcpPageToolResult
{
    /// <summary>
    /// Gets or sets the text the tool answered, or the reason it failed.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the result reports a failure.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Creates the result of a tool that answered.
    /// </summary>
    /// <param name="text">The text the tool answered.</param>
    /// <returns>The result.</returns>
    public static WebMcpPageToolResult Success(string text)
    {
        return new WebMcpPageToolResult
        {
            Text = text ?? string.Empty,
        };
    }

    /// <summary>
    /// Creates the result of a tool that failed or did not answer.
    /// </summary>
    /// <param name="text">The reason, safe to show to the model.</param>
    /// <returns>The result.</returns>
    public static WebMcpPageToolResult Failure(string text)
    {
        return new WebMcpPageToolResult
        {
            Text = text,
            IsError = true,
        };
    }
}
