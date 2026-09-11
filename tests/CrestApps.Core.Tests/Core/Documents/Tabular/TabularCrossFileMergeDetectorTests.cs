using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Verifies <see cref="TabularCrossFileMergeDetector"/> steers a hand-merge of two files toward
/// <c>compare_tabular_data</c> at exactly the point it becomes observable — the second key/measure
/// result from a different document in the same turn — and stays silent everywhere else.
/// </summary>
public sealed class TabularCrossFileMergeDetectorTests
{
    private static readonly TabularQueryResult KeyMeasureResult = new()
    {
        Columns = ["Client", "Sept_Revenue"],
        Rows = [["Northwind", 1700000.00]],
    };

    private static readonly TabularTableInfo ClientServicesTable = new()
    {
        TableName = "Client_Services_Projections_By_Client",
        SourceDocumentId = "document-a",
    };

    private static readonly TabularTableInfo ClientBreakdownTable = new()
    {
        TableName = "Client_Breakdown",
        SourceDocumentId = "document-b",
    };

    [Fact]
    public void Track_WithNoActiveInvocationScope_ReturnsNull()
    {
        var guidance = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Client, Sept_Revenue FROM Client_Services_Projections_By_Client",
            [ClientServicesTable]);

        Assert.Null(guidance);
    }

    [Fact]
    public void Track_ForTheFirstDocumentThisTurn_ReturnsNull()
    {
        using var scope = AIInvocationScope.Begin();

        var guidance = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Client, Sept_Revenue FROM Client_Services_Projections_By_Client",
            [ClientServicesTable]);

        Assert.Null(guidance);
    }

    /// <summary>
    /// The exact behavior the merge guard exists for: a second aggregated result from a different
    /// uploaded file in the same turn is the moment a hand merge is about to happen, so this must steer
    /// toward compare_tabular_data.
    /// </summary>
    [Fact]
    public void Track_WhenASecondDifferentDocumentAppears_ReturnsGuidance()
    {
        using var scope = AIInvocationScope.Begin();

        var first = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Client, Sept_Revenue FROM Client_Services_Projections_By_Client",
            [ClientServicesTable]);

        var second = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Campaign AS Client, Total_Revenue AS Sept_Revenue FROM Client_Breakdown",
            [ClientBreakdownTable]);

        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Contains(TabularToolNames.CompareTabularData, second);
    }

    /// <summary>
    /// Re-running the same document to harmonize its own key names is a normal step while assembling a
    /// comparison, not a hand merge, and must not be interrupted.
    /// </summary>
    [Fact]
    public void Track_WhenTheSameDocumentIsQueriedAgain_ReturnsNull()
    {
        using var scope = AIInvocationScope.Begin();

        TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Client, Sept_Revenue FROM Client_Services_Projections_By_Client",
            [ClientServicesTable]);

        var repeat = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Client, Sept_Revenue FROM Client_Services_Projections_By_Client WHERE Client LIKE '%Lilly%'",
            [ClientServicesTable]);

        Assert.Null(repeat);
    }

    [Fact]
    public void Track_WithAResultThatIsNotKeyMeasureShaped_ReturnsNull()
    {
        using var scope = AIInvocationScope.Begin();

        var threeColumnResult = new TabularQueryResult
        {
            Columns = ["Client", "Site", "Sept_Revenue"],
            Rows = [["Northwind", "Eastport", 1700000.00]],
        };

        var guidance = TabularCrossFileMergeDetector.Track(
            threeColumnResult,
            "SELECT Client, Site, Sept_Revenue FROM Client_Services_Projections_By_Client",
            [ClientServicesTable]);

        Assert.Null(guidance);
    }

    /// <summary>
    /// A query that already joins both documents in one SQL statement is the outcome the guard steers
    /// toward, so it records both documents as seen and says nothing — and a later single-document
    /// result from either side is then treated as already-reconciled, not as a fresh hand merge.
    /// </summary>
    [Fact]
    public void Track_WhenOneQueryAlreadyJoinsBothDocuments_StaysSilentNowAndLater()
    {
        using var scope = AIInvocationScope.Begin();

        var joined = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT cs.Client, cb.Total_Revenue FROM Client_Services_Projections_By_Client cs JOIN Client_Breakdown cb ON cs.Client = cb.Campaign",
            [ClientServicesTable, ClientBreakdownTable]);

        var afterward = TabularCrossFileMergeDetector.Track(
            KeyMeasureResult,
            "SELECT Campaign AS Client, Total_Revenue AS Sept_Revenue FROM Client_Breakdown",
            [ClientBreakdownTable]);

        Assert.Null(joined);
        Assert.Null(afterward);
    }
}
