namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One link on a page: a web address, a jump to another page, or an action.
/// </summary>
internal sealed class PdfLinkEntry
{
    /// <summary>
    /// A link to a web page.
    /// </summary>
    public const string WebKind = "web";

    /// <summary>
    /// A link that writes an email.
    /// </summary>
    public const string EmailKind = "email";

    /// <summary>
    /// A link to a page of the same document.
    /// </summary>
    public const string InternalKind = "internal";

    /// <summary>
    /// A link to another PDF file.
    /// </summary>
    public const string OtherFileKind = "other_file";

    /// <summary>
    /// A link to a file embedded in the document.
    /// </summary>
    public const string EmbeddedFileKind = "embedded_file";

    /// <summary>
    /// A link that opens a file or runs a program.
    /// </summary>
    public const string LaunchKind = "launch";

    /// <summary>
    /// A link that runs a script.
    /// </summary>
    public const string ScriptKind = "javascript";

    /// <summary>
    /// A link that runs a viewer command, such as going to the next page.
    /// </summary>
    public const string NamedActionKind = "named_action";

    /// <summary>
    /// A link whose target is some other action.
    /// </summary>
    public const string OtherKind = "other";

    /// <summary>
    /// Gets the one-based page the link is on.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets the link's kind.
    /// </summary>
    public string Kind { get; init; }

    /// <summary>
    /// Gets the text under the link.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the target as the file states it: an address, a file name, a destination name or a script.
    /// </summary>
    public string Target { get; init; }

    /// <summary>
    /// Gets the page an internal link jumps to, when it could be resolved.
    /// </summary>
    public int? TargetPage { get; init; }

    /// <summary>
    /// Gets where the link is, in user space.
    /// </summary>
    public PdfBox Box { get; init; }

    /// <summary>
    /// Gets or sets the result of checking the link: <c>ok</c>, <c>risky</c>, <c>invalid</c>, <c>broken</c> or <c>unusual</c>.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets why the check came out as it did.
    /// </summary>
    public string Note { get; set; }

    /// <summary>
    /// Describes the text under the link for an answer.
    /// </summary>
    /// <returns>The text in quotes, or a note that the link covers no text.</returns>
    public string DescribeText()
    {
        return string.IsNullOrWhiteSpace(Text)
            ? "(no text under it)"
            : "\"" + Text + "\"";
    }
}
