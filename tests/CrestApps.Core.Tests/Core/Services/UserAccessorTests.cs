using System.Security.Claims;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class UserAccessorTests
{
    [Fact]
    public void User_WhenNothingWasAssigned_FallsBackToTheHttpRequestPrincipal()
    {
        // Arrange
        var requestUser = CreatePrincipal("request-user");
        var accessor = new UserAccessor(CreateHttpContextAccessor(requestUser));

        // Act
        var user = accessor.User;

        // Assert
        Assert.Same(requestUser, user);
    }

    [Fact]
    public void User_WhenThereIsNoHttpContext_ReturnsNull()
    {
        // Arrange
        var accessor = new UserAccessor(new HttpContextAccessor());

        // Act
        var user = accessor.User;

        // Assert
        Assert.Null(accessor.User);
    }

    [Fact]
    public void User_WhenAssigned_OverridesTheHttpRequestPrincipal()
    {
        // Arrange
        var hubUser = CreatePrincipal("hub-user");
        var accessor = new UserAccessor(CreateHttpContextAccessor(CreatePrincipal("request-user")))
        {
            User = hubUser,
        };

        // Act
        var user = accessor.User;

        // Assert
        Assert.Same(hubUser, user);
    }

    [Fact]
    public void User_WhenAssignedNull_FallsBackToTheHttpRequestPrincipal()
    {
        // Arrange
        var requestUser = CreatePrincipal("request-user");
        var accessor = new UserAccessor(CreateHttpContextAccessor(requestUser))
        {
            User = CreatePrincipal("hub-user"),
        };

        // Act
        accessor.User = null;

        // Assert
        Assert.Same(requestUser, accessor.User);
    }

    [Fact]
    public async Task User_WhenAssigned_FlowsAcrossAsynchronousContinuations()
    {
        // Arrange
        var hubUser = CreatePrincipal("hub-user");
        var accessor = new UserAccessor(new HttpContextAccessor())
        {
            User = hubUser,
        };

        // Act
        await Task.Yield();

        var observedUser = await Task.Run(() => accessor.User, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(hubUser, observedUser);
    }

    [Fact]
    public async Task User_WhenAssignedInsideAnAsynchronousFlow_DoesNotLeakToTheCaller()
    {
        // Arrange
        var accessor = new UserAccessor(new HttpContextAccessor());

        // Act
        var observedUser = await AssignAndObserveAsync(accessor, CreatePrincipal("hub-user"));

        // Assert
        Assert.NotNull(observedUser);
        Assert.Null(accessor.User);
    }

    [Fact]
    public async Task User_IsIsolatedBetweenConcurrentFlows()
    {
        // Arrange
        var accessor = new UserAccessor(new HttpContextAccessor());
        var firstUser = CreatePrincipal("first-user");
        var secondUser = CreatePrincipal("second-user");

        // Act
        var results = await Task.WhenAll(
            AssignAndObserveAsync(accessor, firstUser),
            AssignAndObserveAsync(accessor, secondUser));

        // Assert
        Assert.Same(firstUser, results[0]);
        Assert.Same(secondUser, results[1]);
        Assert.Null(accessor.User);
    }

    private static async Task<ClaimsPrincipal> AssignAndObserveAsync(UserAccessor accessor, ClaimsPrincipal user)
    {
        accessor.User = user;

        await Task.Delay(10, TestContext.Current.CancellationToken);

        return accessor.User;
    }

    private static HttpContextAccessor CreateHttpContextAccessor(ClaimsPrincipal user)
    {
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = user,
            },
        };
    }

    private static ClaimsPrincipal CreatePrincipal(string name)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "Test"));
    }
}
