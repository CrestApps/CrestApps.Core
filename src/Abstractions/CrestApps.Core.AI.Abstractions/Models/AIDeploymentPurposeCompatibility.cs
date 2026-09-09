namespace CrestApps.Core.AI.Models;

/// <summary>
/// Read-time normalization that projects the legacy deployment purpose onto the model capability features
/// stored in <see cref="AIDeploymentMetadata"/>.
/// </summary>
/// <remarks>
/// <para>
/// The purpose enum itself is gone. What remains is the stored data: records written before the change
/// carry a <c>Purpose</c>, <c>Capability</c>, or <c>Type</c> field naming one or more legacy purposes. This
/// class reads those names and turns them into capabilities, every time a deployment is read, so a host
/// that never rewrites its stored JSON stays correct. It is the same kind of legacy-shape absorption the
/// framework already performed for this field, not a data migration.
/// </para>
/// <para>
/// It is load bearing rather than cosmetic. <see cref="AIDeploymentFeatureNames.TextGeneration"/> is
/// opt-out — a deployment that declares no capability metadata at all is assumed to be text capable —
/// while <see cref="AIDeploymentFeatureNames.TextEmbedding"/>,
/// <see cref="AIDeploymentFeatureNames.SpeechToText"/>, <see cref="AIDeploymentFeatureNames.TextToSpeech"/>,
/// <see cref="AIDeploymentFeatureNames.ImageInput"/> and <see cref="AIDeploymentFeatureNames.ImageOutput"/>
/// are opt-in. Without this normalization an existing embedding, transcription, or image deployment would
/// simultaneously vanish from its own picker and appear in the chat picker.
/// </para>
/// </remarks>
public static class AIDeploymentPurposeCompatibility
{
    /// <summary>
    /// The legacy purpose names, their flag values, and the capability each implies.
    /// </summary>
    /// <remarks>
    /// The two legacy enums that ever occupied this field used the same names and the same bit values, so
    /// one table covers the <c>Purpose</c>, <c>Capability</c>, and <c>Type</c> field names alike. Chat and
    /// Utility have no entry here because the capability they imply is conditional; see
    /// <see cref="GetImpliedFeatures"/>.
    /// </remarks>
    private static readonly (int Flag, string Name, string Feature)[] _legacyPurposes =
    [
        (1 << 0, "Chat", null),
        (1 << 1, "Utility", null),
        (1 << 2, "Embedding", AIDeploymentFeatureNames.TextEmbedding),
        (1 << 3, "Image", AIDeploymentFeatureNames.ImageOutput),
        (1 << 4, "SpeechToText", AIDeploymentFeatureNames.SpeechToText),
        (1 << 5, "TextToSpeech", AIDeploymentFeatureNames.TextToSpeech),
        (1 << 6, "Vision", AIDeploymentFeatureNames.ImageInput),
    ];

    /// <summary>
    /// Gets the capability features implied by the given legacy purpose names that are not already declared.
    /// </summary>
    /// <param name="legacyPurposes">
    /// The legacy purpose names read from storage. Each entry may itself be a comma-separated list of names
    /// or a numeric flags value, which is how a flags enum round-trips through JSON.
    /// </param>
    /// <param name="declaredFeatures">The features the deployment already declares, if any.</param>
    /// <returns>The features to add, in a stable order. Empty when nothing is implied.</returns>
    /// <remarks>
    /// The projection is additive and conditional. The opt-in features are added whenever the corresponding
    /// legacy name is present — nothing in the legacy shape could have meant to deny them — while
    /// <see cref="AIDeploymentFeatureNames.TextGeneration"/> is added only when the deployment does not
    /// declare <see cref="AIDeploymentFeatureNames.Realtime"/>. Skipping deployments that already carry
    /// metadata would be wrong: a deployment created by the editors gets tool calling and streaming by
    /// default and so would never receive text embedding.
    /// </remarks>
    public static IReadOnlyList<string> GetImpliedFeatures(IEnumerable<string> legacyPurposes, IEnumerable<string> declaredFeatures)
    {
        var purposes = ParseLegacyPurposes(legacyPurposes);

        if (purposes.Count == 0)
        {
            return [];
        }

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (declaredFeatures is not null)
        {
            foreach (var feature in declaredFeatures)
            {
                if (!string.IsNullOrWhiteSpace(feature))
                {
                    declared.Add(feature);
                }
            }
        }

        List<string> implied = null;

        foreach (var (_, name, feature) in _legacyPurposes)
        {
            if (feature is not null && purposes.Contains(name) && !declared.Contains(feature))
            {
                (implied ??= []).Add(feature);
            }
        }

        // textGeneration is conditional, not blind. A speech-to-speech-only deployment was stored as the Chat
        // purpose with the realtime feature and deliberately no textGeneration: it serves only the realtime
        // WebSocket API and answers a text completion with an HTTP 400. Re-adding textGeneration here would
        // re-break the audio-only mode the realtime design depends on.
        if ((purposes.Contains("Chat") || purposes.Contains("Utility")) &&
            !declared.Contains(AIDeploymentFeatureNames.TextGeneration) &&
            !declared.Contains(AIDeploymentFeatureNames.Realtime))
        {
            (implied ??= []).Add(AIDeploymentFeatureNames.TextGeneration);
        }

        return implied ?? [];
    }

    /// <summary>
    /// Merges the features implied by the given legacy purpose names into the deployment's
    /// <see cref="AIDeploymentMetadata"/>.
    /// </summary>
    /// <param name="deployment">The deployment to normalize.</param>
    /// <param name="legacyPurposes">
    /// The legacy purpose names. When <see langword="null"/>, the names captured while the deployment was
    /// deserialized are used.
    /// </param>
    /// <returns><see langword="true"/> when the deployment's metadata was changed.</returns>
    /// <remarks>
    /// Metadata is only created when at least one feature is implied. A deployment whose stored purpose
    /// implies nothing keeps its unconstrained state, which is what keeps
    /// <see cref="AIDeploymentFeatureNames.TextGeneration"/> opt-out for it.
    /// </remarks>
    public static bool Normalize(AIDeployment deployment, IEnumerable<string> legacyPurposes = null)
    {
        if (deployment is null)
        {
            return false;
        }

        // Properties is settable and callers do set it to null — ConfigurationAIDeploymentSource leaves it
        // null for a configured deployment that declares none, and a stored record can carry a JSON null.
        // Restore it before the extensible-entity accessors, which dereference it unconditionally.
        deployment.Properties ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var hasMetadata = deployment.TryGet<AIDeploymentMetadata>(out var metadata);
        var implied = GetImpliedFeatures(legacyPurposes ?? deployment.LegacyPurposes, hasMetadata ? metadata.Features : null);

        // The legacy purpose has now done its one job. Forgetting it here is what keeps the projection a
        // one-time translation rather than a standing rule: a later update that removes a capability must
        // not have it quietly restored by the purpose the record was originally written with.
        deployment.ClearLegacyPurposes();

        if (implied.Count == 0)
        {
            return false;
        }

        metadata ??= new AIDeploymentMetadata();

        metadata.Features = metadata.Features is { Length: > 0 }
            ? [.. metadata.Features, .. implied]
            : [.. implied];

        deployment.Put(metadata);

        return true;
    }

    /// <summary>
    /// Reduces raw legacy purpose values to the set of canonical names they name.
    /// </summary>
    /// <remarks>
    /// A flags enum reaches JSON either as a name, as a comma-separated list of names, or as the numeric
    /// value, and every one of those shapes exists in stored data. All three are accepted here so that no
    /// host is left behind by the shape its serializer happened to write.
    /// </remarks>
    private static HashSet<string> ParseLegacyPurposes(IEnumerable<string> legacyPurposes)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (legacyPurposes is null)
        {
            return names;
        }

        foreach (var value in legacyPurposes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var flags))
                {
                    foreach (var (flag, name, _) in _legacyPurposes)
                    {
                        if ((flags & flag) == flag)
                        {
                            names.Add(name);
                        }
                    }

                    continue;
                }

                foreach (var (_, name, _) in _legacyPurposes)
                {
                    if (string.Equals(part, name, StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
        }

        return names;
    }
}
