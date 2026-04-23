using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.Templates.Providers;

internal static class TemplateProviderConventions
{
    public const string KindMetadataKey = "Kind";
    public const string SystemPromptKind = "SystemPrompt";
    private const string PromptsPathSegment = "Prompts.";

    public static Template CreateTemplate(
        string id,
        TemplateParseResult parseResult,
        string source,
        string featureId = null,
        string defaultKind = null)
    {
        var metadata = parseResult.Metadata ?? new TemplateMetadata();
        var template = new Template
        {
            Id = id,
            Metadata = metadata,
            Content = parseResult.Body,
            Kind = ResolveKind(metadata, defaultKind),
            Source = source,
            FeatureId = featureId,
        };

        if (string.IsNullOrWhiteSpace(template.Metadata.Title))
        {
            template.Metadata.Title = id.Replace('-', ' ').Replace('.', ' ');
        }

        return template;
    }

    public static string ResolveEmbeddedTemplateId(string relativeResourcePath, out string defaultKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeResourcePath);

        defaultKind = null;

        if (relativeResourcePath.StartsWith(PromptsPathSegment, StringComparison.OrdinalIgnoreCase))
        {
            defaultKind = SystemPromptKind;

            return relativeResourcePath[PromptsPathSegment.Length..];
        }

        return relativeResourcePath;
    }

    private static string ResolveKind(TemplateMetadata metadata, string defaultKind)
    {
        if (metadata?.AdditionalProperties != null &&
            metadata.AdditionalProperties.Remove(KindMetadataKey, out var kind) &&
            !string.IsNullOrWhiteSpace(kind))
        {
            return kind;
        }

        return defaultKind;
    }
}
