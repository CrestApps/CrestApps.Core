namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class AIDataSourceLayoutTests
{
    [Fact]
    public void CreateView_ShouldRenderSourceSettingsBeforeKnowledgeBaseSection()
    {
        var content = File.ReadAllText(GetRepositoryPath("src", "Startup", "CrestApps.Core.Blazor.Web", "Components", "Pages", "DataSources", "AIDataSources", "Create.razor"));

        var sourceHeaderIndex = content.IndexOf("<h5>Source</h5>", StringComparison.Ordinal);
        var elasticSettingsIndex = content.IndexOf("<h5>Elasticsearch Source Settings</h5>", StringComparison.Ordinal);
        var knowledgeBaseHeaderIndex = content.IndexOf("<h5>Knowledge Base</h5>", StringComparison.Ordinal);

        Assert.True(sourceHeaderIndex >= 0);
        Assert.True(elasticSettingsIndex > sourceHeaderIndex);
        Assert.True(knowledgeBaseHeaderIndex > elasticSettingsIndex);
    }

    [Fact]
    public void EditView_ShouldRenderSourceSettingsBeforeKnowledgeBaseSection()
    {
        var content = File.ReadAllText(GetRepositoryPath("src", "Startup", "CrestApps.Core.Blazor.Web", "Components", "Pages", "DataSources", "AIDataSources", "Edit.razor"));

        var sourceHeaderIndex = content.IndexOf("<h5>Source</h5>", StringComparison.Ordinal);
        var azureSettingsIndex = content.IndexOf("<h5>Azure AI Search Source Settings</h5>", StringComparison.Ordinal);
        var knowledgeBaseHeaderIndex = content.IndexOf("<h5>Knowledge Base</h5>", StringComparison.Ordinal);

        Assert.True(sourceHeaderIndex >= 0);
        Assert.True(azureSettingsIndex > sourceHeaderIndex);
        Assert.True(knowledgeBaseHeaderIndex > azureSettingsIndex);
    }

    [Fact]
    public void IndexView_ShouldRenderSourceColumn()
    {
        var content = File.ReadAllText(GetRepositoryPath("src", "Startup", "CrestApps.Core.Blazor.Web", "Components", "Pages", "DataSources", "AIDataSources", "Index.razor"));

        Assert.Contains("<th>Source</th>", content, StringComparison.Ordinal);
        Assert.Contains("@GetSourceDisplayName(item)", content, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find '{Path.Combine(relativeSegments)}' from '{AppContext.BaseDirectory}'.");
    }
}
