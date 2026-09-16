using System.Security.Claims;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Startup.Shared.Areas.AIChat.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

public sealed class KnowledgeFigureAuthorizationTests
{
    /// <summary>
    /// Verifies that the user who created the data source may download its figures, whether the data source
    /// recorded the owner identifier or only the author name.
    /// </summary>
    [Fact]
    public async Task HandleAsync_OwnerOfTheDataSource_Succeeds()
    {
        var byOwnerId = await AuthorizeAsync(
            CreatePrincipal("user-1", "Sample User"),
            CreateDataSource(ownerId: "user-1", author: "Someone Else"));

        Assert.True(byOwnerId);

        var byAuthor = await AuthorizeAsync(
            CreatePrincipal("user-1", "Sample User"),
            CreateDataSource(ownerId: null, author: "Sample User"));

        Assert.True(byAuthor);
    }

    /// <summary>
    /// Verifies that a user in the Administrator role may download figures from a data source somebody else
    /// owns, which is how the sample sites' single administrator account reaches everything it created.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AdministratorRole_Succeeds()
    {
        var granted = await AuthorizeAsync(
            CreatePrincipal("user-2", "Another User", "Administrator"),
            CreateDataSource(ownerId: "user-1", author: "Sample User"));

        Assert.True(granted);
    }

    /// <summary>
    /// Verifies the fix: a signed-in user who neither owns the data source nor administers the site is not
    /// granted the figures of someone else's data source.
    /// </summary>
    [Fact]
    public async Task HandleAsync_SignedInButUnrelatedUser_DoesNotSucceed()
    {
        var granted = await AuthorizeAsync(
            CreatePrincipal("user-2", "Another User"),
            CreateDataSource(ownerId: "user-1", author: "Sample User"));

        Assert.False(granted);
    }

    /// <summary>
    /// Verifies that a data source which recorded neither an owner nor an author belongs to nobody rather
    /// than to everybody, so an empty identifier never matches an empty one.
    /// </summary>
    [Fact]
    public async Task HandleAsync_UnownedDataSource_DoesNotSucceed()
    {
        var granted = await AuthorizeAsync(
            CreatePrincipal(userId: null, name: null),
            CreateDataSource(ownerId: null, author: null));

        Assert.False(granted);
    }

    /// <summary>
    /// Verifies that the handler stays fail-closed for an anonymous caller, for a missing resource and for an
    /// unrelated operation, returning without granting rather than throwing.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AnonymousOrMissingResourceOrOtherOperation_DoesNotSucceed()
    {
        var anonymous = await AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            CreateDataSource(ownerId: "user-1", author: "Sample User"));

        Assert.False(anonymous);

        var withoutResource = await AuthorizeAsync(
            CreatePrincipal("user-1", "Sample User", "Administrator"),
            resource: null);

        Assert.False(withoutResource);

        var otherOperation = await AuthorizeAsync(
            CreatePrincipal("user-1", "Sample User", "Administrator"),
            CreateDataSource(ownerId: "user-1", author: "Sample User"),
            new OperationAuthorizationRequirement
            {
                Name = "SomeOtherOperation",
            });

        Assert.False(otherOperation);
    }

    private static async Task<bool> AuthorizeAsync(
        ClaimsPrincipal user,
        AIDataSource resource,
        OperationAuthorizationRequirement requirement = null)
    {
        requirement ??= AIKnowledgeOperations.ViewFigures;

        var context = new AuthorizationHandlerContext([requirement], user, resource);

        await new SampleKnowledgeFigureAuthorizationHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    private static AIDataSource CreateDataSource(string ownerId, string author)
    {
        return new AIDataSource
        {
            ItemId = "data-source-1",
            DisplayText = "Sample data source",
            Source = AIDataSourceSourceTypes.File,
            OwnerId = ownerId,
            Author = author,
        };
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string name, string role = null)
    {
        var claims = new List<Claim>();

        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        if (!string.IsNullOrEmpty(name))
        {
            claims.Add(new Claim(ClaimTypes.Name, name));
        }

        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
