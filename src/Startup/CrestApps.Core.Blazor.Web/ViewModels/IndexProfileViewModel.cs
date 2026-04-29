using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class IndexProfileViewModel
{
    public string ItemId { get; set; }

    public string Name { get; set; }

    public string DisplayText { get; set; }

    public string IndexName { get; set; }

    public string ProviderName { get; set; }

    public string Type { get; set; }

    public string EmbeddingDeploymentName { get; set; }

    public IReadOnlyList<IndexProfileSourceDescriptor> Sources { get; set; } = [];

    public List<KeyValuePair<string, string>> Providers { get; set; } = [];

    public List<KeyValuePair<string, string>> Types { get; set; } = [];

    public List<KeyValuePair<string, string>> EmbeddingDeployments { get; set; } = [];

    public static IndexProfileViewModel FromProfile(SearchIndexProfile profile)
    {
        return new IndexProfileViewModel
        {
            ItemId = profile.ItemId,
            Name = profile.Name,
            DisplayText = profile.DisplayText,
            IndexName = profile.IndexName,
            ProviderName = profile.ProviderName,
            Type = profile.Type,
            EmbeddingDeploymentName = profile.EmbeddingDeploymentName,
        };
    }

    public void ApplyTo(SearchIndexProfile profile)
    {
        profile.Name = Name?.Trim();
        profile.DisplayText = DisplayText?.Trim();
        profile.IndexName = IndexName?.Trim();
        profile.ProviderName = ProviderName;
        profile.Type = Type;
        profile.EmbeddingDeploymentName = EmbeddingDeploymentName;
    }
}
