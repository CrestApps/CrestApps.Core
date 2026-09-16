namespace CrestApps.Core.AI.Mcp.Knowledge;

/// <summary>
/// The resource type and URI template an ingested figure is published under.
/// </summary>
public static class DataSourceFigureResourceConstants
{
    /// <summary>
    /// The resource type.
    /// </summary>
    public const string Type = "datasource-figure";

    /// <summary>
    /// The URI template a figure is addressed by. It is the same address a search result carries, so a
    /// client can read a figure straight out of what a search told it.
    /// </summary>
    public const string UriTemplate = "crestapps://datasource/{dataSourceId}/figure/{figureId}";
}
