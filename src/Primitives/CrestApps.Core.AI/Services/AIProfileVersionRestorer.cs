using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Restores an AI profile from a saved <see cref="AIProfileVersion"/>.
/// </summary>
public static class AIProfileVersionRestorer
{
    /// <summary>
    /// Copies what a saved version holds onto the profile, keeping what identifies the profile: its identifier,
    /// technical name, type, source, owner, creation time and version number. Save the profile afterwards to keep the restore as a new
    /// version; set <see cref="AIProfileVersionMetadata.PendingNote"/> first to note where it came from.
    /// </summary>
    /// <param name="profile">The profile to restore.</param>
    /// <param name="snapshot">The profile as the version saved it.</param>
    public static void Restore(AIProfile profile, AIProfile snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        var copy = snapshot.Clone();
        var version = profile.TryGet<AIProfileVersionMetadata>(out var metadata)
            ? metadata
            : null;

        profile.DisplayText = copy.DisplayText;
        profile.Description = copy.Description;
        profile.OrchestratorName = copy.OrchestratorName;
        profile.ChatDeploymentName = copy.ChatDeploymentName;
        profile.UtilityDeploymentName = copy.UtilityDeploymentName;
        profile.TitleType = copy.TitleType;
        profile.WelcomeMessage = copy.WelcomeMessage;
        profile.PromptSubject = copy.PromptSubject;
        profile.PromptTemplate = copy.PromptTemplate;
        profile.Properties = copy.Properties;
        profile.Settings = copy.Settings;

        // The version number belongs to the profile's history, not to the snapshot.
        if (version is not null)
        {
            profile.Put(version);
        }
        else
        {
            profile.Properties.Remove(nameof(AIProfileVersionMetadata));
        }
    }
}
