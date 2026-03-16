using LetterTranslation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LetterTranslation.Api.Controllers;

[ApiController]
[Route("api")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IDataService _dataService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUserService userService, IDataService dataService, ILogger<AuthController> logger)
    {
        _userService = userService;
        _dataService = dataService;
        _logger = logger;
    }

    [Authorize]
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync()
    {
        _logger.LogInformation("Processing login request.");

        // The email claim is usually mapped to http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress
        var emailClaim = User.FindFirst(ClaimTypes.Email);
        var email = emailClaim?.Value;
        
        if (!_userService.IsUserAllowed(email))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Unauthorized", message = $"Access denied for {email ?? "unknown user"}." });
        }

        // Get the Google User ID (sub claim is often mapped to NameIdentifier)
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            // Log all claims to help debug if the claim type is different
            foreach (var claim in User.Claims)
            {
                _logger.LogDebug("Claim: {Type} = {Value}", claim.Type, claim.Value);
            }
            _logger.LogError("Could not find user ID (NameIdentifier/sub claim) for email {Email}.", email);
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        try
        {
            await _dataService.InitializeUserWorkspaceAsync(userId, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize workspace for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "InternalServerError", message = "Failed to initialize user storage." });
        }

        _logger.LogInformation("Login successful for user '{Email}' (ID: {UserId}).", email, userId);
        return Ok(new { message = "Login successful", userId = userId });
    }

    [Authorize]
    [HttpGet("secure-data")]
    public IActionResult GetSecureData()
    {
        var emailClaim = User.FindFirst(ClaimTypes.Email);
        var email = emailClaim?.Value;
        
        if (!_userService.IsUserAllowed(email))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Unauthorized" });
        }

        return Ok(new { message = $"Hello, {email}. You are an authorized user!" });
    }
}
