namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Allocates a unique SQLite table name for a worksheet being imported into a tabular workspace. The
/// workspace supplies the implementation so table naming and de-duplication stay centralized, leaving
/// importers responsible only for parsing and streaming rows.
/// </summary>
/// <param name="worksheetName">
/// The source worksheet name, or <see langword="null"/> for single-sheet sources such as CSV or TSV
/// files that have no worksheet concept.
/// </param>
/// <param name="singleWorksheet">Whether the source document contains a single worksheet.</param>
/// <returns>A unique SQLite table name.</returns>
public delegate string TabularTableNameAllocator(string worksheetName, bool singleWorksheet);
