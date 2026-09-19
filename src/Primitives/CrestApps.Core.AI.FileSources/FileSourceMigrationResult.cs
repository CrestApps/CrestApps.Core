namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// What one migration pass moved.
/// </summary>
/// <param name="FileSourcesMoved">How many records were moved out of the web crawler store.</param>
/// <param name="ItemStatesMoved">How many per-item state records were moved out of the crawl state store.</param>
public readonly record struct FileSourceMigrationResult(int FileSourcesMoved, int ItemStatesMoved);
