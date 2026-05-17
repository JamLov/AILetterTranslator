using System.Security.Claims;
using FluentAssertions;
using LetterTranslation.Api.Filters;
using LetterTranslation.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace LetterTranslation.Api.UnitTests.Filters;

public class AllowedUserFilterTests
{
    private readonly Mock<IUserService> _userService = new();

    private ActionExecutingContext CreateContext(ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext();
        if (user != null) httpContext.User = user;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());
    }

    private static ClaimsPrincipal AuthenticatedUser(string email) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.NameIdentifier, "sub123")
        }, "Bearer"));

    [Fact]
    public async Task AllowedUser_ProceedsToAction()
    {
        _userService.Setup(x => x.IsUserAllowed("allowed@test.com")).Returns(true);
        var filter = new AllowedUserFilter(_userService.Object);
        var context = CreateContext(AuthenticatedUser("allowed@test.com"));
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task DisallowedUser_Returns403()
    {
        _userService.Setup(x => x.IsUserAllowed("banned@test.com")).Returns(false);
        var filter = new AllowedUserFilter(_userService.Object);
        var context = CreateContext(AuthenticatedUser("banned@test.com"));
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ProceedsToAction()
    {
        var filter = new AllowedUserFilter(_userService.Object);
        var context = CreateContext(); // no user / not authenticated
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
    }
}
