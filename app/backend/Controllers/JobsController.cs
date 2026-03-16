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
    private readonly IProjectService _projectService;
    private readonly ILogger<JobsController> _logger;

    private const int MaxJobNameLength = 250;
    private const int MaxNotesLength = 1000;
    private const long MaxFileSizeBytes = 4 * 1024 * 1024; // 4 MB
    private readonly string[] AllowedContentTypes = { "image/jpeg", "image/png" };

    public JobsController(IUserService userService, IDataService dataService, IProjectService projectService, ILogger<JobsController> logger)
    {
        _userService = userService;
        _dataService = dataService;
        _projectService = projectService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetJobsAsync()
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        _logger.LogInformation("Listing jobs for user {UserId}", userId);

        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        var jobs = await _dataService.GetJobsAsync(userId);
        _logger.LogInformation("Returning {Count} job(s) for user {UserId}", jobs.Count(), userId);
        return Ok(jobs);
    }

    [HttpGet("{jobId}")]
    public async Task<IActionResult> GetJobDetailAsync(Guid jobId)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        _logger.LogInformation("Getting job detail for job {JobId}, user {UserId}", jobId, userId);

        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        var jobDetail = await _dataService.GetJobDetailAsync(userId, jobId);

        if (jobDetail == null)
        {
            _logger.LogInformation("Job {JobId} not found for user {UserId}", jobId, userId);
            return NotFound();
        }

        _logger.LogInformation("Returning job detail for {JobId} (status: {Status})", jobId, jobDetail.Metadata.Status);
        return Ok(jobDetail);
    }
    
    [HttpPost("{jobId}/reset")]
    public async Task<IActionResult> ResetJobAsync(Guid jobId)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        _logger.LogInformation("Reset requested for job {JobId}, user {UserId}", jobId, userId);

        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });
        }

        var result = await _dataService.ResetJobAsync(userId, jobId);

        if (!result)
        {
            _logger.LogInformation("Reset failed - job {JobId} not found for user {UserId}", jobId, userId);
            return NotFound();
        }

        _logger.LogInformation("Job {JobId} reset successfully", jobId);
        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> CreateJobAsync([FromForm] CreateJobRequest request)
    {
        _logger.LogInformation("Create job request from {Email}", User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown");

        var emailClaim = User.FindFirst(ClaimTypes.Email);
        var email = emailClaim?.Value;

        if (!_userService.IsUserAllowed(email))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Unauthorized", message = $"Access denied for {email ?? "unknown user"}." });
        }

        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;
        
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
                _logger.LogInformation("File rejected: {FileName} exceeds 4MB ({Size} bytes)", file.FileName, file.Length);
                return BadRequest(new { error = "InvalidFile", message = $"File '{file.FileName}' exceeds the maximum allowed size of 4MB." });
            }
            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                _logger.LogInformation("File rejected: {FileName} has unsupported type {ContentType}", file.FileName, file.ContentType);
                return BadRequest(new { error = "InvalidFile", message = $"File '{file.FileName}' has an unsupported type. Only JPG and PNG are allowed." });
            }
        }

        _logger.LogInformation("Creating job '{JobName}' with {FileCount} file(s) for user {UserId}", request.JobName, request.Files.Count, userId);

        try
        {
            var jobMetadata = await _dataService.CreateJobAsync(userId, request);
            _logger.LogInformation("Job {JobId} created successfully", jobMetadata.JobId);
            return Created($"/api/jobs/{jobMetadata.JobId}", jobMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create job for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "InternalServerError", message = "An error occurred while creating the job." });
        }
    }

    [HttpPatch("{jobId}/metadata")]
    public async Task<IActionResult> UpdateJobMetadataAsync(Guid jobId, [FromBody] UpdateJobMetadataRequest request)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;

        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (request.LetterDate != null && !DateOnly.TryParse(request.LetterDate, out _))
            return BadRequest(new { error = "InvalidRequest", message = "LetterDate must be a valid date in YYYY-MM-DD format." });

        var result = await _dataService.UpdateJobLetterDateAsync(userId, jobId, request.LetterDate);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{jobId}/move-to-project/{projectId}")]
    public async Task<IActionResult> MoveToProjectAsync(Guid jobId, Guid projectId)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;

        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var result = await _projectService.MoveJobToProjectAsync(userId, jobId, projectId);
        if (!result)
            return NotFound(new { error = "NotFound", message = "Job or project not found, or you are not the project owner." });

        return NoContent();
    }

    [HttpDelete("{jobId}")]
    public async Task<IActionResult> DeleteJobAsync(Guid jobId)
    {
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        var userId = subClaim?.Value;

        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var result = await _dataService.DeleteJobAsync(userId, jobId);
        if (!result) return NotFound();
        return NoContent();
    }
}
