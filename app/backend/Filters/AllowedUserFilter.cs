using System.Security.Claims;
using LetterTranslation.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LetterTranslation.Api.Filters;

/// <summary>
/// Global action filter that enforces the AllowedUsers whitelist on every request.
/// Runs after JWT authentication, ensuring removed users lose access immediately
/// rather than waiting for token expiry.
/// </summary>
public class AllowedUserFilter : IAsyncActionFilter
{
    private readonly IUserService _userService;

    public AllowedUserFilter(IUserService userService)
    {
        _userService = userService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Only enforce on authenticated requests (let [AllowAnonymous] endpoints pass)
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            var email = context.HttpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            if (!_userService.IsUserAllowed(email))
            {
                context.Result = new ObjectResult(new { error = "Forbidden", message = "User is not in the allowed list." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }
        }

        await next();
    }
}
