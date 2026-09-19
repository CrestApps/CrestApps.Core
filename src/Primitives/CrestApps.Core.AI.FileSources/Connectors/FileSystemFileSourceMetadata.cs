namespace CrestApps.Core.AI.FileSources.Connectors;

/// <summary>
/// The settings a file-system file source carries.
/// </summary>
/// <remarks>
/// It is persisted under its short type name, inside the record's properties.
/// </remarks>
public sealed class FileSystemFileSourceMetadata
{
    /// <summary>
    /// Gets or sets the folder to read. Everything below is relative to this, never to the host's allowed
    /// root, so a file source pointed at <c>App_Data/file-sources/test</c> reads <c>test</c> and nothing
    /// beside it.
    /// </summary>
    public string RootPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sub-folders of <see cref="RootPath"/> are read too.
    /// <see langword="false"/> reads only the files sitting directly in <see cref="RootPath"/>.
    /// </summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// Gets or sets the most files one listing may return, or <see langword="null"/> for the host default.
    /// </summary>
    public int? MaxItems { get; set; }
}
