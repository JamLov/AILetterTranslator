using FluentAssertions;
using LetterTranslation.Api.Controllers;
using LetterTranslation.Api.Models;
using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace LetterTranslation.Api.UnitTests.Controllers;

public class JobsControllerTests
{
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<IDataService> _dataServiceMock;
    private readonly Mock<IProjectService> _projectServiceMock;
    private readonly Mock<ILogger<JobsController>> _loggerMock;
    private readonly JobsController _controller;

    public JobsControllerTests()
    {
        _userServiceMock = new Mock<IUserService>();
        _dataServiceMock = new Mock<IDataService>();
        _projectServiceMock = new Mock<IProjectService>();
        _loggerMock = new Mock<ILogger<JobsController>>();

        _controller = new JobsController(_userServiceMock.Object, _dataServiceMock.Object, _projectServiceMock.Object, _loggerMock.Object);
    }

    private void SetUserContext(string email, string nameIdentifier)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(email)) claims.Add(new Claim(ClaimTypes.Email, email));
        if (!string.IsNullOrEmpty(nameIdentifier)) claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));

        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    [Fact]
    public async Task CreateJobAsync_WhenUserIsNotAllowed_ReturnsForbidden()
    {
        // Arrange
        SetUserContext("unauthorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("unauthorized@example.com")).Returns(false);
        var request = new CreateJobRequest { JobName = "Test" };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task CreateJobAsync_WhenJobNameIsMissing_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);
        var request = new CreateJobRequest { JobName = "" };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateJobAsync_WhenJobNameIsTooLong_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);
        var request = new CreateJobRequest { JobName = new string('A', 251) };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateJobAsync_WhenNotesAreTooLong_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);
        var request = new CreateJobRequest { JobName = "Valid", Notes = new string('A', 1001) };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateJobAsync_WhenFilesAreMissing_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);
        var request = new CreateJobRequest { JobName = "Test Job", Files = new List<IFormFile>() };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateJobAsync_WhenFileIsTooLarge_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns((4 * 1024 * 1024) + 1); // 4MB + 1 byte

        var request = new CreateJobRequest 
        { 
            JobName = "Test Job", 
            Files = new List<IFormFile> { mockFile.Object } 
        };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateJobAsync_WhenFileHasInvalidType_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.ContentType).Returns("text/plain"); // Invalid

        var request = new CreateJobRequest 
        { 
            JobName = "Test Job", 
            Files = new List<IFormFile> { mockFile.Object } 
        };

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateJobAsync_WithValidRequest_CreatesJobAndReturnsCreated()
    {
        // Arrange
        var email = "authorized@example.com";
        var userId = "user123";
        SetUserContext(email, userId);
        _userServiceMock.Setup(s => s.IsUserAllowed(email)).Returns(true);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");

        var request = new CreateJobRequest 
        { 
            JobName = "Test Job", 
            Files = new List<IFormFile> { mockFile.Object } 
        };

        var createdMetadata = new JobMetadata
        {
            JobId = Guid.NewGuid(), // Now returns a Guid
            JobName = request.JobName,
            CreatedAt = DateTime.UtcNow,
            Status = "Not Started",
            OriginalFileCount = request.Files.Count
        };
        _dataServiceMock.Setup(s => s.CreateJobAsync(userId, request)).ReturnsAsync(createdMetadata);

        // Act
        var result = await _controller.CreateJobAsync(request);

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(result);
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.Value.Should().BeEquivalentTo(createdMetadata);

        _dataServiceMock.Verify(s => s.CreateJobAsync(userId, request), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_WhenUserHasNoJobs_ReturnsEmptyList()
    {
        // Arrange
        var userId = "user123";
        SetUserContext("authorized@example.com", userId);
        _userServiceMock.Setup(s => s.IsUserAllowed(It.IsAny<string>())).Returns(true);
        _dataServiceMock.Setup(s => s.GetJobsAsync(userId)).ReturnsAsync(Enumerable.Empty<JobMetadata>());

        // Act
        var result = await _controller.GetJobsAsync();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var jobs = Assert.IsAssignableFrom<IEnumerable<JobMetadata>>(okResult.Value);
        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetJobsAsync_WhenUserHasJobs_ReturnsListOfJobs()
    {
        // Arrange
        var userId = "user123";
        SetUserContext("authorized@example.com", userId);
        _userServiceMock.Setup(s => s.IsUserAllowed(It.IsAny<string>())).Returns(true);

        var mockJobs = new List<JobMetadata>
        {
            new JobMetadata { JobId = Guid.NewGuid(), JobName = "Job 1", Status = "Finished", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new JobMetadata { JobId = Guid.NewGuid(), JobName = "Job 2", Status = "In Progress", CreatedAt = DateTime.UtcNow }
        };
        _dataServiceMock.Setup(s => s.GetJobsAsync(userId)).ReturnsAsync(mockJobs);

        // Act
        var result = await _controller.GetJobsAsync();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var jobs = Assert.IsAssignableFrom<IEnumerable<JobMetadata>>(okResult.Value);
        jobs.Should().HaveCount(2);
        jobs.Should().BeEquivalentTo(mockJobs, options => options.ComparingByMembers<JobMetadata>());
    }

    [Fact]
    public async Task GetJobsAsync_WhenUserIdIsMissing_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", null); // Missing userId
        _userServiceMock.Setup(s => s.IsUserAllowed(It.IsAny<string>())).Returns(true);

        // Act
        var result = await _controller.GetJobsAsync();

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ResetJobAsync_WhenUserIdIsMissing_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", null);

        var result = await _controller.ResetJobAsync(Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResetJobAsync_WhenJobNotFound_ReturnsNotFound()
    {
        var userId = "user123";
        SetUserContext("authorized@example.com", userId);
        var jobId = Guid.NewGuid();

        _dataServiceMock.Setup(s => s.ResetJobAsync(userId, jobId)).ReturnsAsync(false);

        var result = await _controller.ResetJobAsync(jobId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ResetJobAsync_WhenSuccessful_ReturnsNoContent()
    {
        var userId = "user123";
        SetUserContext("authorized@example.com", userId);
        var jobId = Guid.NewGuid();

        _dataServiceMock.Setup(s => s.ResetJobAsync(userId, jobId)).ReturnsAsync(true);

        var result = await _controller.ResetJobAsync(jobId);

        Assert.IsType<NoContentResult>(result);
        _dataServiceMock.Verify(s => s.ResetJobAsync(userId, jobId), Times.Once);
    }

    #region GetJobDetailAsync

    [Fact]
    public async Task GetJobDetailAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", null);

        var result = await _controller.GetJobDetailAsync(Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetJobDetailAsync_WhenJobNotFound_ReturnsNotFound()
    {
        var userId = "user123";
        SetUserContext("authorized@example.com", userId);
        var jobId = Guid.NewGuid();

        _dataServiceMock.Setup(s => s.GetJobDetailAsync(userId, jobId)).ReturnsAsync((JobDetail?)null);

        var result = await _controller.GetJobDetailAsync(jobId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetJobDetailAsync_WhenFound_ReturnsOk()
    {
        var userId = "user123";
        SetUserContext("authorized@example.com", userId);
        var jobId = Guid.NewGuid();

        var detail = new JobDetail
        {
            Metadata = new JobMetadata { JobId = jobId, JobName = "Test", Status = "Finished" },
            OriginalFileNames = new List<string> { "test.jpg" }
        };
        _dataServiceMock.Setup(s => s.GetJobDetailAsync(userId, jobId)).ReturnsAsync(detail);

        var result = await _controller.GetJobDetailAsync(jobId);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().Be(detail);
    }

    #endregion

    #region UpdateJobMetadataAsync

    [Fact]
    public async Task UpdateJobMetadataAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", null);

        var result = await _controller.UpdateJobMetadataAsync(Guid.NewGuid(), new UpdateJobMetadataRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateJobMetadataAsync_WhenInvalidDate_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", "user123");

        var result = await _controller.UpdateJobMetadataAsync(Guid.NewGuid(),
            new UpdateJobMetadataRequest { LetterDate = "not-a-date" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateJobMetadataAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("authorized@example.com", "user123");
        var jobId = Guid.NewGuid();
        _dataServiceMock.Setup(s => s.UpdateJobLetterDateAsync("user123", jobId, "2026-01-15")).ReturnsAsync(false);

        var result = await _controller.UpdateJobMetadataAsync(jobId,
            new UpdateJobMetadataRequest { LetterDate = "2026-01-15" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateJobMetadataAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("authorized@example.com", "user123");
        var jobId = Guid.NewGuid();
        _dataServiceMock.Setup(s => s.UpdateJobLetterDateAsync("user123", jobId, "2026-01-15")).ReturnsAsync(true);

        var result = await _controller.UpdateJobMetadataAsync(jobId,
            new UpdateJobMetadataRequest { LetterDate = "2026-01-15" });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateJobMetadataAsync_WithNullDate_ReturnsNoContent()
    {
        SetUserContext("authorized@example.com", "user123");
        var jobId = Guid.NewGuid();
        _dataServiceMock.Setup(s => s.UpdateJobLetterDateAsync("user123", jobId, (string?)null)).ReturnsAsync(true);

        var result = await _controller.UpdateJobMetadataAsync(jobId,
            new UpdateJobMetadataRequest { LetterDate = null });

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region MoveToProjectAsync

    [Fact]
    public async Task MoveToProjectAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", null);

        var result = await _controller.MoveToProjectAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task MoveToProjectAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("authorized@example.com", "user123");
        _projectServiceMock.Setup(s => s.MoveJobToProjectAsync("user123", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(false);

        var result = await _controller.MoveToProjectAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task MoveToProjectAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("authorized@example.com", "user123");
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.MoveJobToProjectAsync("user123", jobId, projectId)).ReturnsAsync(true);

        var result = await _controller.MoveToProjectAsync(jobId, projectId);

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region DeleteJobAsync

    [Fact]
    public async Task DeleteJobAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", null);

        var result = await _controller.DeleteJobAsync(Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("authorized@example.com", "user123");
        var jobId = Guid.NewGuid();
        _dataServiceMock.Setup(s => s.DeleteJobAsync("user123", jobId)).ReturnsAsync(false);

        var result = await _controller.DeleteJobAsync(jobId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteJobAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("authorized@example.com", "user123");
        var jobId = Guid.NewGuid();
        _dataServiceMock.Setup(s => s.DeleteJobAsync("user123", jobId)).ReturnsAsync(true);

        var result = await _controller.DeleteJobAsync(jobId);

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region CreateJobAsync - additional scenarios

    [Fact]
    public async Task CreateJobAsync_WhenDataServiceThrows_Returns500()
    {
        SetUserContext("authorized@example.com", "user123");
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");

        var request = new CreateJobRequest
        {
            JobName = "Test",
            Files = new List<IFormFile> { mockFile.Object }
        };
        _dataServiceMock.Setup(s => s.CreateJobAsync("user123", request)).ThrowsAsync(new IOException("disk full"));

        var result = await _controller.CreateJobAsync(request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task CreateJobAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("authorized@example.com", null);
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);

        var result = await _controller.CreateJobAsync(new CreateJobRequest { JobName = "Test" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    #endregion

    #region Versioning

    [Fact]
    public async Task GetJobVersionsAsync_WhenNotFound_Returns404()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.GetJobVersionsAsync("user-1", It.IsAny<Guid>()))
            .ReturnsAsync((IEnumerable<VersionSummary>?)null);

        var result = await _controller.GetJobVersionsAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetJobVersionsAsync_HappyPath_ReturnsOk()
    {
        SetUserContext("user@example.com", "user-1");
        var jobId = Guid.NewGuid();
        var versions = new List<VersionSummary>
        {
            new() { VersionNumber = 2, IsCurrent = true, ProcessingMode = "TranscriptionEdit" },
            new() { VersionNumber = 1, IsCurrent = false, ProcessingMode = "Initial" }
        };
        _dataServiceMock.Setup(s => s.GetJobVersionsAsync("user-1", jobId)).ReturnsAsync(versions);

        var result = await _controller.GetJobVersionsAsync(jobId);

        var ok = Assert.IsType<OkObjectResult>(result);
        ok.Value.Should().BeEquivalentTo(versions);
    }

    [Fact]
    public async Task CreateJobVersionAsync_WithEmptyEditedMarkdown_Returns400()
    {
        SetUserContext("user@example.com", "user-1");

        var result = await _controller.CreateJobVersionAsync(Guid.NewGuid(),
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "  " });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateJobVersionAsync_WithOversizedMarkdown_Returns400()
    {
        SetUserContext("user@example.com", "user-1");

        var result = await _controller.CreateJobVersionAsync(Guid.NewGuid(),
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = new string('x', 200 * 1024 + 1) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateJobVersionAsync_WhenInvalidMode_Returns400()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.CreateJobVersionAsync("user-1", It.IsAny<Guid>(), It.IsAny<CreateVersionRequest>()))
            .ReturnsAsync((null, "InvalidMode"));

        var result = await _controller.CreateJobVersionAsync(Guid.NewGuid(),
            new CreateVersionRequest { Mode = "Bad", EditedMarkdown = "x" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateJobVersionAsync_WhenNotFound_Returns404()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.CreateJobVersionAsync("user-1", It.IsAny<Guid>(), It.IsAny<CreateVersionRequest>()))
            .ReturnsAsync((null, "NotFound"));

        var result = await _controller.CreateJobVersionAsync(Guid.NewGuid(),
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "x" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CreateJobVersionAsync_WhenConflict_Returns409()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.CreateJobVersionAsync("user-1", It.IsAny<Guid>(), It.IsAny<CreateVersionRequest>()))
            .ReturnsAsync((null, "Conflict"));

        var result = await _controller.CreateJobVersionAsync(Guid.NewGuid(),
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "x" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateJobVersionAsync_HappyPath_Returns202WithLatestVersion()
    {
        SetUserContext("user@example.com", "user-1");
        var metadata = new JobMetadata
        {
            JobId = Guid.NewGuid(), JobName = "x", CreatedAt = DateTime.UtcNow,
            Status = "Not Started", LatestVersionNumber = 2
        };
        _dataServiceMock.Setup(s => s.CreateJobVersionAsync("user-1", It.IsAny<Guid>(), It.IsAny<CreateVersionRequest>()))
            .ReturnsAsync((metadata, null));

        var result = await _controller.CreateJobVersionAsync(Guid.NewGuid(),
            new CreateVersionRequest { Mode = "TranscriptionEdit", EditedMarkdown = "x" });

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<CreateVersionResponse>(accepted.Value);
        response.LatestVersionNumber.Should().Be(2);
        response.Status.Should().Be("Not Started");
    }

    [Fact]
    public async Task RevertJobVersionAsync_WhenNotFound_Returns404()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.RevertJobVersionAsync("user-1", It.IsAny<Guid>())).ReturnsAsync(false);

        var result = await _controller.RevertJobVersionAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RevertJobVersionAsync_HappyPath_Returns204()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.RevertJobVersionAsync("user-1", It.IsAny<Guid>())).ReturnsAsync(true);

        var result = await _controller.RevertJobVersionAsync(Guid.NewGuid());

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GetJobSourceAsync_WhenNotFound_Returns404()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.GetJobSourceAsync("user-1", It.IsAny<Guid>(), "transcribed")).ReturnsAsync((string?)null);

        var result = await _controller.GetJobSourceAsync(Guid.NewGuid(), "transcribed");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetJobSourceAsync_HappyPath_Returns200WithContent()
    {
        SetUserContext("user@example.com", "user-1");
        _dataServiceMock.Setup(s => s.GetJobSourceAsync("user-1", It.IsAny<Guid>(), "transcribed")).ReturnsAsync("raw markdown");

        var result = await _controller.GetJobSourceAsync(Guid.NewGuid(), "transcribed");

        var ok = Assert.IsType<OkObjectResult>(result);
        ok.Value.Should().BeEquivalentTo(new { content = "raw markdown" });
    }

    #endregion
}

