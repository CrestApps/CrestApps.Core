namespace CrestApps.Core.Startup.Shared.Realtime;

/// <summary>
/// The voice identifiers exposed by the OpenAI / Azure OpenAI <c>gpt-realtime</c> family. The realtime API
/// validates the session voice against this fixed, model-defined set (there is no enumeration API), so the list
/// is maintained here as the single source of truth shared by the MVC and Blazor test playgrounds.
/// </summary>
public static class RealtimeVoices
{
    /// <summary>Gets the available realtime voice identifiers, in alphabetical order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "alloy",
        "ash",
        "ballad",
        "cedar",
        "coral",
        "echo",
        "marin",
        "sage",
        "shimmer",
        "verse",
    ];
}
