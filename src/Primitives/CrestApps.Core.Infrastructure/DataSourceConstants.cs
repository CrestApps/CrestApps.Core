namespace CrestApps.Core.Infrastructure;

/// <summary>
/// Provides functionality for data Source Constants.
/// </summary>
public static class DataSourceConstants
{
    public const string IndexingTaskType = "DataSourceIndex";

    /// <summary>
    /// Provides functionality for column Names.
    /// </summary>
    public static class ColumnNames
    {
        public const string ReferenceId = "referenceId";

        public const string DataSourceId = "dataSourceId";

        public const string ChunkId = "chunkId";

        public const string ChunkIndex = "chunkIndex";

        public const string Title = "title";

        public const string Content = "content";

        public const string Embedding = "embedding";

        public const string Timestamp = "timestamp";

        public const string ReferenceType = "referenceType";

        /// <summary>
        /// Discriminates what kind of knowledge a row holds. A row written before this column existed has
        /// no value, and every reader treats that as text.
        /// </summary>
        public const string ContentType = "contentType";

        /// <summary>
        /// The canonical identifier of the document a row ultimately belongs to.
        /// </summary>
        public const string RootId = "rootId";

        /// <summary>
        /// The canonical identifier of the object a row hangs directly off, so a hit can be widened.
        /// </summary>
        public const string ParentId = "parentId";

        /// <summary>
        /// The page a row was read from.
        /// </summary>
        public const string Page = "page";

        public const string Filters = "filters";

        private static readonly HashSet<string> _typedColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            ContentType,
            RootId,
            ParentId,
            Page,
        };

        /// <summary>
        /// Determines whether a field name is one of the names the knowledge base keeps for its typed
        /// columns, without saying anything about the scope the name was written in.
        /// </summary>
        /// <param name="name">The field name as written in the filter.</param>
        /// <returns><see langword="true"/> when the name is a reserved typed column name.</returns>
        public static bool IsReservedColumnName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _typedColumns.Contains(name.Trim());
        }

        /// <summary>
        /// Determines whether a field named in a caller's filter is one of the typed knowledge columns rather
        /// than an entry in the per-row filter bag.
        /// </summary>
        /// <param name="name">The field name as written in the filter.</param>
        /// <param name="knowledgeScope">
        /// Whether the name is read in the knowledge base's own scope. A field the caller addressed through
        /// the filter bag is not, and neither is a filter written against a data source that stores no typed
        /// knowledge of its own.
        /// </param>
        /// <returns><see langword="true"/> when the name is a typed column.</returns>
        /// <remarks>
        /// Every provider stores caller-supplied fields in a <see cref="Filters"/> bag and translates a filter
        /// field into a lookup there. The typed discriminators are real columns, so a filter on them has to
        /// reach the column or it matches nothing. The scope is what stops that from claiming the names for
        /// everyone: a data source whose own documents have carried a top-level <c>contentType</c> since long
        /// before these columns existed keeps meaning its own field, because its filter addresses the bag
        /// rather than the knowledge scope.
        /// </remarks>
        public static bool IsTypedColumn(string name, bool knowledgeScope)
        {
            return knowledgeScope && IsReservedColumnName(name);
        }

        /// <summary>
        /// Determines whether a filter field was addressed through the per-row filter bag rather than as a
        /// column of its own.
        /// </summary>
        /// <param name="name">The field name as written in the filter.</param>
        /// <returns><see langword="true"/> when the name addresses the bag.</returns>
        /// <remarks>
        /// Providers spell a nested field differently — a dot for Elasticsearch and PostgreSQL, a slash for
        /// Azure AI Search — so both spellings are read here and a caller only ever has to write one.
        /// </remarks>
        public static bool IsFilterBagField(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                name.Length > Filters.Length + 1 &&
                name.StartsWith(Filters, StringComparison.OrdinalIgnoreCase) &&
                (name[Filters.Length] == '.' || name[Filters.Length] == '/');
        }

        /// <summary>
        /// Addresses a field through the per-row filter bag, so a name that also names a typed column keeps
        /// meaning the caller's own field.
        /// </summary>
        /// <param name="name">The field name as the caller wrote it.</param>
        /// <returns>The field name addressed through the bag.</returns>
        public static string QualifyFilterField(string name)
        {
            return IsFilterBagField(name) ? name : $"{Filters}.{name}";
        }
    }
}
