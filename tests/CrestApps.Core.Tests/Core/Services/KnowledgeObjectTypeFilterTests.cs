using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies how a restriction to particular kinds of knowledge is turned into a filter.
/// </summary>
/// <remarks>
/// Two paths reach a knowledge base with such a restriction -- a tool instance pinned to figures, and a data
/// source attached straight to a profile -- and they used to be able to disagree about what the restriction
/// meant. These are the rules both now share.
/// </remarks>
public sealed class KnowledgeObjectTypeFilterTests
{
    [Fact]
    public void BuildClause_WhenNothingIsRestricted_FiltersNothing()
    {
        Assert.Null(KnowledgeObjectTypeFilter.BuildClause(null));
        Assert.Null(KnowledgeObjectTypeFilter.BuildClause([]));
    }

    [Fact]
    public void BuildClause_WhenOnlyBlanksAreGiven_FiltersNothing()
    {
        // A restriction that names nothing is not a restriction to nothing, which would return no rows at all.
        Assert.Null(KnowledgeObjectTypeFilter.BuildClause(["", "   "]));
    }

    [Fact]
    public void BuildClause_ForOneKind_NeedsNoParentheses()
    {
        Assert.Equal("contentType eq 'figure'", KnowledgeObjectTypeFilter.BuildClause([KnowledgeObjectTypes.Figure]));
    }

    [Fact]
    public void BuildClause_ForSeveralKinds_JoinsThemAsAlternatives()
    {
        Assert.Equal(
            "(contentType eq 'figure' or contentType eq 'chart')",
            KnowledgeObjectTypeFilter.BuildClause([KnowledgeObjectTypes.Figure, KnowledgeObjectTypes.Chart]));
    }

    [Fact]
    public void BuildClause_AskingForText_AlsoAdmitsRowsWithNoKind()
    {
        // Every row written before typed knowledge existed carries no kind, and every reader treats those as
        // text. Without this, "only text" returns nothing on exactly the corpus that is entirely text.
        Assert.Equal(
            "(contentType eq 'text' or contentType eq null)",
            KnowledgeObjectTypeFilter.BuildClause([KnowledgeObjectTypes.Text]));
    }

    [Fact]
    public void BuildClause_AskingForTextAmongOthers_StillAdmitsRowsWithNoKind()
    {
        var clause = KnowledgeObjectTypeFilter.BuildClause([KnowledgeObjectTypes.Text, KnowledgeObjectTypes.Table]);

        Assert.Contains("contentType eq null", clause, StringComparison.Ordinal);
        Assert.Contains("contentType eq 'table'", clause, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClause_NotAskingForText_DoesNotAdmitRowsWithNoKind()
    {
        var clause = KnowledgeObjectTypeFilter.BuildClause([KnowledgeObjectTypes.Figure]);

        Assert.DoesNotContain("null", clause, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClause_EscapesAQuoteRatherThanEndingTheLiteral()
    {
        // The kinds are constants today, but the value reaches here from a stored setting.
        var clause = KnowledgeObjectTypeFilter.BuildClause(["it's"]);

        Assert.Equal("contentType eq 'it''s'", clause);
    }

    [Fact]
    public void Combine_WithNoClause_LeavesTheCallersFilterAlone()
    {
        Assert.Equal("page eq 8", KnowledgeObjectTypeFilter.Combine("page eq 8", null));
        Assert.Equal("page eq 8", KnowledgeObjectTypeFilter.Combine("page eq 8", "   "));
    }

    [Fact]
    public void Combine_WithNoCallerFilter_IsTheClauseAlone()
    {
        Assert.Equal("contentType eq 'figure'", KnowledgeObjectTypeFilter.Combine(null, "contentType eq 'figure'"));
    }

    [Fact]
    public void Combine_BracketsTheCallersFilterBeforeAnding()
    {
        // Without the brackets a caller's "a or b" would bind loosely and widen the restriction instead of
        // narrowing it.
        Assert.Equal(
            "(a or b) and contentType eq 'figure'",
            KnowledgeObjectTypeFilter.Combine("a or b", "contentType eq 'figure'"));
    }

    [Fact]
    public void Admits_WhenNothingIsRestricted_AdmitsEveryKind()
    {
        Assert.True(KnowledgeObjectTypeFilter.Admits(null, KnowledgeObjectTypes.Figure));
        Assert.True(KnowledgeObjectTypeFilter.Admits([], KnowledgeObjectTypes.Text));
    }

    [Fact]
    public void Admits_ReadsTheRestrictionRegardlessOfCasing()
    {
        Assert.True(KnowledgeObjectTypeFilter.Admits(["FIGURE"], KnowledgeObjectTypes.Figure));
    }

    [Fact]
    public void Admits_RefusesAKindTheRestrictionLeavesOut()
    {
        Assert.False(KnowledgeObjectTypeFilter.Admits([KnowledgeObjectTypes.Text], KnowledgeObjectTypes.Figure));
        Assert.False(KnowledgeObjectTypeFilter.Admits([KnowledgeObjectTypes.Figure], KnowledgeObjectTypes.Chart));
    }
}
