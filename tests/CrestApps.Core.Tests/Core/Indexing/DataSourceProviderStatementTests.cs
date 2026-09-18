using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.PostgreSQL.Services;

namespace CrestApps.Core.Tests.Core.Indexing;

/// <summary>
/// Covers the statements each index provider had to grow for typed knowledge: topping up a schema on an
/// index that predates it, and deleting a reference's rows by predicate rather than by a thousand guessed
/// identifiers. Both are string building, and both are wrong in ways a live index would only reveal later.
/// </summary>
public sealed class DataSourceProviderStatementTests
{
    /// <summary>
    /// Verifies that PostgreSQL adds a column only when it is missing, which is what makes the top-up safe
    /// to run on every synchronization.
    /// </summary>
    [Fact]
    public void PostgreSQL_AddColumnStatement_IsIdempotent()
    {
        var statement = PostgreSQLSearchIndexManager.BuildAddColumnStatement(
            "\"kb_index\"",
            new SearchIndexField
            {
                Name = "contentType",
                FieldType = SearchFieldType.Keyword,
            });

        Assert.Equal("ALTER TABLE \"kb_index\" ADD COLUMN IF NOT EXISTS \"contentType\" TEXT", statement);
    }

    /// <summary>
    /// Verifies that each field type becomes the column type it can actually hold, so a page number is
    /// filterable as a number rather than compared as text.
    /// </summary>
    [Theory]
    [InlineData(SearchFieldType.Integer, "INTEGER")]
    [InlineData(SearchFieldType.Float, "REAL")]
    [InlineData(SearchFieldType.DateTime, "TIMESTAMPTZ")]
    [InlineData(SearchFieldType.Keyword, "TEXT")]
    [InlineData(SearchFieldType.Text, "TEXT")]
    public void PostgreSQL_AddColumnStatement_MapsFieldTypes(SearchFieldType fieldType, string expected)
    {
        var statement = PostgreSQLSearchIndexManager.BuildAddColumnStatement(
            "\"kb_index\"",
            new SearchIndexField
            {
                Name = "page",
                FieldType = fieldType,
            });

        Assert.EndsWith(" " + expected, statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the delete filter names both the data source and the references, so an identifier two
    /// data sources happen to share can never delete the wrong one's rows.
    /// </summary>
    [Fact]
    public void AzureAISearch_ReferenceIdFilter_IsScopedToTheDataSource()
    {
        var filter = AzureAISearchDataSourceContentManager.BuildReferenceIdFilter(
            "data-source-1",
            ["doc-1", "doc-2"]);

        Assert.Equal(
            "dataSourceId eq 'data-source-1' and (referenceId eq 'doc-1' or referenceId eq 'doc-2')",
            filter);
    }

    /// <summary>
    /// Verifies that an identifier containing a space or a comma still selects exactly its own rows.
    /// </summary>
    /// <remarks>
    /// This is why the filter compares the references one by one. The <c>search.in</c> function takes a
    /// single delimited string rather than a list of arguments, and its default delimiters are the space and
    /// the comma, so a reference identifier built from a file name was split into fragments that matched
    /// nothing. The stale rows then survived a re-index while the delete reported success.
    /// </remarks>
    [Fact]
    public void AzureAISearch_ReferenceIdFilter_SurvivesSpacesAndCommas()
    {
        var filter = AzureAISearchDataSourceContentManager.BuildReferenceIdFilter(
            "data-source-1",
            ["document:Q3 Report, final.pdf"]);

        Assert.Equal(
            "dataSourceId eq 'data-source-1' and (referenceId eq 'document:Q3 Report, final.pdf')",
            filter);
    }

    /// <summary>
    /// Verifies that an apostrophe in an identifier is escaped rather than ending the literal. A reference
    /// identifier is whatever the source called the item, and a file name may contain anything.
    /// </summary>
    [Fact]
    public void AzureAISearch_ReferenceIdFilter_EscapesQuotes()
    {
        var filter = AzureAISearchDataSourceContentManager.BuildReferenceIdFilter(
            "data-source-1",
            ["o'brien.pdf"]);

        Assert.Contains("'o''brien.pdf'", filter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that references are deleted in batches rather than in one filter of unbounded length, which
    /// a search service refuses.
    /// </summary>
    [Fact]
    public void AzureAISearch_ReferenceIdBatchSize_IsBounded()
    {
        Assert.InRange(AzureAISearchDataSourceContentManager.ReferenceIdBatchSize, 1, 1000);
    }
}
