namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIDataSourceViewLayoutTests
{
    [Fact]
    public void CreateView_ShouldRenderSourceSettingsBeforeKnowledgeBaseSection()
    {
        var content = File.ReadAllText(GetRepositoryPath("src", "Startup", "CrestApps.Core.Mvc.Web", "Areas", "DataSources", "Views", "AIDataSource", "Create.cshtml"));

        var sourceHeaderIndex = content.IndexOf("<h5>Source</h5>", StringComparison.Ordinal);
        var elasticSettingsIndex = content.IndexOf("id=\"elasticsearch-source-settings\"", StringComparison.Ordinal);
        var knowledgeBaseHeaderIndex = content.IndexOf("<h5>Knowledge Base</h5>", StringComparison.Ordinal);

        Assert.True(sourceHeaderIndex >= 0);
        Assert.True(elasticSettingsIndex > sourceHeaderIndex);
        Assert.True(knowledgeBaseHeaderIndex > elasticSettingsIndex);
    }

    [Fact]
    public void EditView_ShouldRenderSourceSettingsBeforeKnowledgeBaseSection()
    {
        var content = File.ReadAllText(GetRepositoryPath("src", "Startup", "CrestApps.Core.Mvc.Web", "Areas", "DataSources", "Views", "AIDataSource", "Edit.cshtml"));

        var sourceHeaderIndex = content.IndexOf("<h5>Source</h5>", StringComparison.Ordinal);
        var azureSettingsIndex = content.IndexOf("id=\"azure-ai-search-source-settings\"", StringComparison.Ordinal);
        var knowledgeBaseHeaderIndex = content.IndexOf("<h5>Knowledge Base</h5>", StringComparison.Ordinal);

        Assert.True(sourceHeaderIndex >= 0);
        Assert.True(azureSettingsIndex > sourceHeaderIndex);
        Assert.True(knowledgeBaseHeaderIndex > azureSettingsIndex);
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
