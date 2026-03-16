using LetterTranslation.Api.Models;
using LetterTranslation.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LetterTranslation.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IProjectService _projectService;
    private readonly ILogger<ProjectsController> _logger;

    private const int MaxNameLength = 250;
    private const int MaxDescriptionLength = 1000;
    private const int MaxNotesLength = 1000;
    private const long MaxFileSizeBytes = 4 * 1024 * 1024;
    private readonly string[] AllowedContentTypes = { "image/jpeg", "image/png" };

    public ProjectsController(IUserService userService, IProjectService projectService, ILogger<ProjectsController> logger)
    {
        _userService = userService;
        _projectService = projectService;
        _logger = logger;
    }

    private string? GetUserId() => (User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub"))?.Value;

    [HttpGet]
    public async Task<IActionResult> GetProjectsAsync()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var projects = await _projectService.GetProjectsAsync(userId);
        return Ok(projects);
    }

    [HttpPost]
    public async Task<IActionResult> CreateProjectAsync([FromBody] CreateProjectRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "InvalidRequest", message = "Name is required." });
        if (request.Name.Length > MaxNameLength)
            return BadRequest(new { error = "InvalidRequest", message = $"Name cannot exceed {MaxNameLength} characters." });
        if (request.Description != null && request.Description.Length > MaxDescriptionLength)
            return BadRequest(new { error = "InvalidRequest", message = $"Description cannot exceed {MaxDescriptionLength} characters." });

        var project = await _projectService.CreateProjectAsync(userId, request);
        return Created($"/api/projects/{project.ProjectId}", project);
    }

    [HttpGet("{projectId}")]
    public async Task<IActionResult> GetProjectDetailAsync(Guid projectId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var detail = await _projectService.GetProjectDetailAsync(userId, projectId);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpPut("{projectId}")]
    public async Task<IActionResult> UpdateProjectAsync(Guid projectId, [FromBody] UpdateProjectRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (request.Name != null && request.Name.Length > MaxNameLength)
            return BadRequest(new { error = "InvalidRequest", message = $"Name cannot exceed {MaxNameLength} characters." });
        if (request.Description != null && request.Description.Length > MaxDescriptionLength)
            return BadRequest(new { error = "InvalidRequest", message = $"Description cannot exceed {MaxDescriptionLength} characters." });

        var result = await _projectService.UpdateProjectAsync(userId, projectId, request);
        if (result == null)
            return NotFound(new { error = "NotFound", message = "Project not found or you are not the owner." });
        return Ok(result);
    }

    [HttpDelete("{projectId}")]
    public async Task<IActionResult> DeleteProjectAsync(Guid projectId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var result = await _projectService.DeleteProjectAsync(userId, projectId);
        if (!result)
            return Conflict(new { error = "DeleteFailed", message = "Project not found, you are not the owner, or the project still contains jobs." });
        return NoContent();
    }

    [HttpPost("{projectId}/jobs")]
    public async Task<IActionResult> CreateProjectJobAsync(Guid projectId, [FromForm] CreateJobRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (string.IsNullOrWhiteSpace(request.JobName))
            return BadRequest(new { error = "InvalidRequest", message = "JobName is required." });
        if (request.JobName.Length > MaxNameLength)
            return BadRequest(new { error = "InvalidRequest", message = $"JobName cannot exceed {MaxNameLength} characters." });
        if (request.Notes != null && request.Notes.Length > MaxNotesLength)
            return BadRequest(new { error = "InvalidRequest", message = $"Notes cannot exceed {MaxNotesLength} characters." });
        if (request.Files == null || request.Files.Count == 0)
            return BadRequest(new { error = "InvalidRequest", message = "At least one image file must be uploaded." });

        foreach (var file in request.Files)
        {
            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { error = "InvalidFile", message = $"File '{file.FileName}' exceeds the maximum allowed size of 4MB." });
            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                return BadRequest(new { error = "InvalidFile", message = $"File '{file.FileName}' has an unsupported type. Only JPG and PNG are allowed." });
        }

        try
        {
            var job = await _projectService.CreateProjectJobAsync(userId, projectId, request);
            return Created($"/api/projects/{projectId}/jobs/{job.JobId}", job);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden", message = "Only the project owner can create jobs." });
        }
    }

    [HttpGet("{projectId}/jobs/{jobId}")]
    public async Task<IActionResult> GetProjectJobDetailAsync(Guid projectId, Guid jobId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var detail = await _projectService.GetProjectJobDetailAsync(userId, projectId, jobId);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpPatch("{projectId}/jobs/{jobId}/metadata")]
    public async Task<IActionResult> UpdateProjectJobMetadataAsync(Guid projectId, Guid jobId, [FromBody] UpdateJobMetadataRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (request.LetterDate != null && !DateOnly.TryParse(request.LetterDate, out _))
            return BadRequest(new { error = "InvalidRequest", message = "LetterDate must be a valid date in YYYY-MM-DD format." });

        var result = await _projectService.UpdateProjectJobLetterDateAsync(userId, projectId, jobId, request.LetterDate);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{projectId}/jobs/{jobId}/reset")]
    public async Task<IActionResult> ResetProjectJobAsync(Guid projectId, Guid jobId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var result = await _projectService.ResetProjectJobAsync(userId, projectId, jobId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpDelete("{projectId}/jobs/{jobId}")]
    public async Task<IActionResult> DeleteProjectJobAsync(Guid projectId, Guid jobId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var result = await _projectService.DeleteProjectJobAsync(userId, projectId, jobId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("{projectId}/members")]
    public async Task<IActionResult> AddMemberAsync(Guid projectId, [FromBody] AddMemberRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "InvalidRequest", message = "Email is required." });

        var (success, error) = await _projectService.AddMemberByEmailAsync(userId, projectId, request.Email.Trim());
        if (!success)
        {
            // Use 404 when the email is not a registered user
            if (error != null && error.Contains("No registered user"))
                return NotFound(new { error = "UserNotFound", message = error });
            return BadRequest(new { error = "AddMemberFailed", message = error });
        }
        return Ok(new { message = "Member added successfully." });
    }

    [HttpPost("{projectId}/members/remove")]
    public async Task<IActionResult> RemoveMemberAsync(Guid projectId, [FromBody] AddMemberRequest request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "InvalidRequest", message = "Email is required." });

        var (success, error) = await _projectService.RemoveMemberByEmailAsync(userId, projectId, request.Email.Trim());
        if (!success)
            return BadRequest(new { error = "RemoveMemberFailed", message = error });
        return Ok(new { message = "Member removed successfully." });
    }

    [HttpPost("{projectId}/jobs/{jobId}/move-to-standalone")]
    public async Task<IActionResult> MoveToStandaloneAsync(Guid projectId, Guid jobId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "InvalidToken", message = "Could not extract user ID from token." });

        var result = await _projectService.MoveJobToStandaloneAsync(userId, projectId, jobId);
        if (!result) return NotFound();
        return NoContent();
    }
}
