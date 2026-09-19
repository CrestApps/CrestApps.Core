using CrestApps.Core.AI.Documents.Models;

namespace CrestApps.Core.Startup.Shared.Services;

internal static class SiteSettingsInteractionDocumentOptionsMapper
{
    public static InteractionDocumentOptions Create(InteractionDocumentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new InteractionDocumentOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(InteractionDocumentSettings settings, InteractionDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
        options.RetrievalMode = settings.RetrievalMode;
        options.AllowDocumentUploads = settings.AllowDocumentUploads;
        options.AllowImageUploads = settings.AllowImageUploads;
        options.MaxIndexableCharacters = settings.MaxIndexableCharacters;
        options.DescribeFiguresInUploads = settings.DescribeFiguresInUploads;
    }
}
