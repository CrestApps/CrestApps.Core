namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Says what an AI tool does to the application's data and to the outside world, so hosts can decide from data
/// which tools need a person's approval and which tools a setting that exposes every tool leaves out.
/// </summary>
public enum AIToolKind
{
    /// <summary>
    /// The tool has no kind. It behaves as it did before kinds existed: it needs an approval only when its
    /// registration asks for one, and a setting that exposes every tool includes it.
    /// </summary>
    Unspecified,

    /// <summary>
    /// The tool returns data and changes nothing.
    /// </summary>
    Read,

    /// <summary>
    /// The tool creates or changes data that can be changed back.
    /// </summary>
    Write,

    /// <summary>
    /// The tool deletes data, or does something that cannot be undone. It needs a person's approval.
    /// </summary>
    Destructive,

    /// <summary>
    /// The tool sends something outside the application (an email, a text message, a call, a webhook), calls a
    /// paid service, or moves money. It needs a person's approval.
    /// </summary>
    External,

    /// <summary>
    /// The tool changes who can access what, how people sign in, secrets, or what code or templates the
    /// application runs. It needs a person's approval.
    /// </summary>
    Security,
}
