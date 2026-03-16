using LetterTranslation.Api.Models;
using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LetterTranslation.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IDataService _dataService;
    private readonly ILogger<JobsController> _logger;

    private const int MaxJobNameLength = 250;
    private const int MaxNotesLength = 1000;
    private const long MaxFileSizeBytes = 4 * 1024 * 1024; // 4 MB
    private readonly string[] AllowedContentTypes = { "image/jpeg", "image/png" };

    public JobsController(IUserService userService, IDataService dataService, ILogger<JobsController> logger)
    {
        _userService = userService;
        _dataService = dataService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetJobsAsync()
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        _logger.LogDebug("GetJobsAsync called for user ID: {UserId}", userId);


        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        var jobs = await _dataService.GetJobsAsync(userId);
        return Ok(jobs);
    }

    [HttpGet("{jobId}")]
    public async Task<IActionResult> GetJobDetailAsync(Guid jobId)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        _logger.LogDebug("GetJobDetailAsync called for user ID: {UserId} and Job ID: {JobId}", userId, jobId);


        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        var jobDetail = await _dataService.GetJobDetailAsync(userId, jobId);

        if (jobDetail == null)
        {
            return NotFound();
        }

        return Ok(jobDetail);
    }
    
    [HttpPost("{jobId}/reset")]
    public async Task<IActionResult> ResetJobAsync(Guid jobId)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        var result = await _dataService.ResetJobAsync(userId, jobId);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> CreateJobAsync([FromForm] CreateJobRequest request)
    {
        _logger.LogInformation("Processing create job request.");

        var emailClaim = User.FindFirst(ClaimTypes.Email);
        var email = emailClaim?.Value;
        
        if (!_userService.IsUserAllowed(email))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Unauthorized", message = $"Access denied for {email ?? "unknown user"}." });
        }

        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        _logger.LogDebug("CreateJobAsync called for user ID: {UserId}", userId);
        
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("Could not find user ID for email {Email}.", email);
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        // Input Validation
        if (string.IsNullOrWhiteSpace(request.JobName))
        {
            return BadRequest(new { error = "InvalidRequest", message = "JobName is required." });
        }
        if (request.JobName.Length > MaxJobNameLength)
        {
            return BadRequest(new { error = "InvalidRequest", message = $"JobName cannot exceed {MaxJobNameLength} characters." });
        }
        if (request.Notes != null && request.Notes.Length > MaxNotesLength)
        {
            return BadRequest(new { error = "InvalidRequest", message = $"Notes cannot exceed {MaxNotesLength} characters." });
        }

        if (request.Files == null || request.Files.Count == 0)
        {
            return BadRequest(new { error = "InvalidRequest", message = "At least one image file must be uploaded." });
        }

        // File Validation
        foreach (var file in request.Files)
        {
            if (file.Length > MaxFileSizeBytes)
            {
                return BadRequest(new { error = "InvalidFile", message = $"File '{file.FileName}' exceeds the maximum allowed size of 4MB." });
            }
            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                return BadRequest(new { error = "InvalidFile", message = $"File '{file.FileName}' has an unsupported type. Only JPG and PNG are allowed." });
            }
        }

        try
        {
            var jobMetadata = await _dataService.CreateJobAsync(userId, request);
            return Created($"/api/jobs/{jobMetadata.JobId}", jobMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create job for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "InternalServerError", message = "An error occurred while creating the job." });
        }
    }
}
