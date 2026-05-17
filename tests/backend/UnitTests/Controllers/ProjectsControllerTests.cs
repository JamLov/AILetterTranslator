using FluentAssertions;
using LetterTranslation.Api.Controllers;
using LetterTranslation.Api.Models;
using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace LetterTranslation.Api.UnitTests.Controllers;

public class ProjectsControllerTests
{
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<IProjectService> _projectServiceMock;
    private readonly Mock<ILogger<ProjectsController>> _loggerMock;
    private readonly ProjectsController _controller;

    public ProjectsControllerTests()
    {
        _userServiceMock = new Mock<IUserService>();
        _projectServiceMock = new Mock<IProjectService>();
        _loggerMock = new Mock<ILogger<ProjectsController>>();

        _controller = new ProjectsController(_userServiceMock.Object, _projectServiceMock.Object, _loggerMock.Object);
    }

    private void SetUserContext(string? email, string? nameIdentifier)
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

    #region GetProjectsAsync

    [Fact]
    public async Task GetProjectsAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.GetProjectsAsync();

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsOkWithProjects()
    {
        SetUserContext("test@example.com", "user-1");
        var projects = new List<ProjectSummary>
        {
            new() { ProjectId = Guid.NewGuid(), Name = "Project 1", IsOwner = true, JobCount = 3 }
        };
        _projectServiceMock.Setup(s => s.GetProjectsAsync("user-1")).ReturnsAsync(projects);

        var result = await _controller.GetProjectsAsync();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsAssignableFrom<IEnumerable<ProjectSummary>>(okResult.Value);
        returned.Should().HaveCount(1);
    }

    #endregion

    #region CreateProjectAsync

    [Fact]
    public async Task CreateProjectAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.CreateProjectAsync(new CreateProjectRequest { Name = "Test" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectAsync_WhenNameMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectAsync(new CreateProjectRequest { Name = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectAsync_WhenNameIsWhitespace_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectAsync(new CreateProjectRequest { Name = "   " });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectAsync_WhenNameTooLong_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectAsync(new CreateProjectRequest { Name = new string('A', 251) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectAsync_WhenDescriptionTooLong_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectAsync(new CreateProjectRequest
        {
            Name = "Valid",
            Description = new string('A', 1001)
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectAsync_WithValidRequest_ReturnsCreated()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        var request = new CreateProjectRequest { Name = "My Project", Description = "A description" };

        _projectServiceMock.Setup(s => s.CreateProjectAsync("user-1", request))
            .ReturnsAsync(new ProjectMetadata { ProjectId = projectId, Name = "My Project" });

        var result = await _controller.CreateProjectAsync(request);

        var createdResult = Assert.IsType<CreatedResult>(result);
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.Location.Should().Contain(projectId.ToString());
    }

    [Fact]
    public async Task CreateProjectAsync_WithNullDescription_ReturnsCreated()
    {
        SetUserContext("test@example.com", "user-1");
        var request = new CreateProjectRequest { Name = "No Desc Project", Description = null };

        _projectServiceMock.Setup(s => s.CreateProjectAsync("user-1", request))
            .ReturnsAsync(new ProjectMetadata { ProjectId = Guid.NewGuid(), Name = "No Desc Project" });

        var result = await _controller.CreateProjectAsync(request);

        Assert.IsType<CreatedResult>(result);
    }

    #endregion

    #region GetProjectDetailAsync

    [Fact]
    public async Task GetProjectDetailAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.GetProjectDetailAsync(Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProjectDetailAsync_WhenProjectNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.GetProjectDetailAsync("user-1", projectId)).ReturnsAsync((ProjectDetail?)null);

        var result = await _controller.GetProjectDetailAsync(projectId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetProjectDetailAsync_WhenFound_ReturnsOk()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        var detail = new ProjectDetail
        {
            Metadata = new ProjectMetadata { ProjectId = projectId, Name = "Test" },
            Jobs = new List<JobMetadata>(),
            IsOwner = true,
            MemberEmails = new List<string>(),
            MemberCount = 0
        };
        _projectServiceMock.Setup(s => s.GetProjectDetailAsync("user-1", projectId)).ReturnsAsync(detail);

        var result = await _controller.GetProjectDetailAsync(projectId);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().Be(detail);
    }

    #endregion

    #region UpdateProjectAsync

    [Fact]
    public async Task UpdateProjectAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.UpdateProjectAsync(Guid.NewGuid(), new UpdateProjectRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProjectAsync_WhenNameTooLong_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.UpdateProjectAsync(Guid.NewGuid(), new UpdateProjectRequest { Name = new string('A', 251) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProjectAsync_WhenDescriptionTooLong_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.UpdateProjectAsync(Guid.NewGuid(), new UpdateProjectRequest { Description = new string('A', 1001) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProjectAsync_WhenNotFoundOrNotOwner_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.UpdateProjectAsync("user-1", projectId, It.IsAny<UpdateProjectRequest>()))
            .ReturnsAsync((ProjectMetadata?)null);

        var result = await _controller.UpdateProjectAsync(projectId, new UpdateProjectRequest { Name = "Updated" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProjectAsync_WhenSuccessful_ReturnsOk()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        var updated = new ProjectMetadata { ProjectId = projectId, Name = "Updated" };
        _projectServiceMock.Setup(s => s.UpdateProjectAsync("user-1", projectId, It.IsAny<UpdateProjectRequest>()))
            .ReturnsAsync(updated);

        var result = await _controller.UpdateProjectAsync(projectId, new UpdateProjectRequest { Name = "Updated" });

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().Be(updated);
    }

    #endregion

    #region DeleteProjectAsync

    [Fact]
    public async Task DeleteProjectAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.DeleteProjectAsync(Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenFails_ReturnsConflict()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.DeleteProjectAsync("user-1", projectId)).ReturnsAsync(false);

        var result = await _controller.DeleteProjectAsync(projectId);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.DeleteProjectAsync("user-1", projectId)).ReturnsAsync(true);

        var result = await _controller.DeleteProjectAsync(projectId);

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region CreateProjectJobAsync

    [Fact]
    public async Task CreateProjectJobAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest { JobName = "Test" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenJobNameMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest { JobName = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenJobNameTooLong_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest { JobName = new string('A', 251) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenNotesTooLong_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest
        {
            JobName = "Valid",
            Notes = new string('A', 1001)
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenNoFiles_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest
        {
            JobName = "Valid",
            Files = new List<IFormFile>()
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenFileTooLarge_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns((4 * 1024 * 1024) + 1);
        mockFile.Setup(f => f.FileName).Returns("big.jpg");

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest
        {
            JobName = "Valid",
            Files = new List<IFormFile> { mockFile.Object }
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenInvalidFileType_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.ContentType).Returns("application/pdf");
        mockFile.Setup(f => f.FileName).Returns("doc.pdf");

        var result = await _controller.CreateProjectJobAsync(Guid.NewGuid(), new CreateJobRequest
        {
            JobName = "Valid",
            Files = new List<IFormFile> { mockFile.Object }
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenNotOwner_ReturnsForbidden()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.FileName).Returns("test.jpg");

        _projectServiceMock.Setup(s => s.CreateProjectJobAsync("user-1", projectId, It.IsAny<CreateJobRequest>()))
            .ThrowsAsync(new UnauthorizedAccessException("Only the project owner can create jobs."));

        var result = await _controller.CreateProjectJobAsync(projectId, new CreateJobRequest
        {
            JobName = "Valid",
            Files = new List<IFormFile> { mockFile.Object }
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WithValidRequest_ReturnsCreated()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.FileName).Returns("test.jpg");

        var metadata = new JobMetadata { JobId = jobId, JobName = "Test Job", Status = "Not Started" };
        _projectServiceMock.Setup(s => s.CreateProjectJobAsync("user-1", projectId, It.IsAny<CreateJobRequest>()))
            .ReturnsAsync(metadata);

        var result = await _controller.CreateProjectJobAsync(projectId, new CreateJobRequest
        {
            JobName = "Test Job",
            Files = new List<IFormFile> { mockFile.Object }
        });

        var createdResult = Assert.IsType<CreatedResult>(result);
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        createdResult.Location.Should().Contain(projectId.ToString());
        createdResult.Location.Should().Contain(jobId.ToString());
    }

    #endregion

    #region GetProjectJobDetailAsync

    [Fact]
    public async Task GetProjectJobDetailAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.GetProjectJobDetailAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProjectJobDetailAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.GetProjectJobDetailAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync((JobDetail?)null);

        var result = await _controller.GetProjectJobDetailAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetProjectJobDetailAsync_WhenFound_ReturnsOk()
    {
        SetUserContext("test@example.com", "user-1");
        var detail = new JobDetail
        {
            Metadata = new JobMetadata { JobId = Guid.NewGuid(), JobName = "Test" },
            OriginalFileNames = new List<string>()
        };
        _projectServiceMock.Setup(s => s.GetProjectJobDetailAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetProjectJobDetailAsync(Guid.NewGuid(), Guid.NewGuid());

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().Be(detail);
    }

    #endregion

    #region UpdateProjectJobMetadataAsync

    [Fact]
    public async Task UpdateProjectJobMetadataAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.UpdateProjectJobMetadataAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobMetadataRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProjectJobMetadataAsync_WhenInvalidDate_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.UpdateProjectJobMetadataAsync(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateJobMetadataRequest { LetterDate = "not-a-date" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProjectJobMetadataAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.UpdateProjectJobLetterDateAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>(), "2026-01-15"))
            .ReturnsAsync(false);

        var result = await _controller.UpdateProjectJobMetadataAsync(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateJobMetadataRequest { LetterDate = "2026-01-15" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateProjectJobMetadataAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.UpdateProjectJobLetterDateAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>(), "2026-01-15"))
            .ReturnsAsync(true);

        var result = await _controller.UpdateProjectJobMetadataAsync(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateJobMetadataRequest { LetterDate = "2026-01-15" });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateProjectJobMetadataAsync_WithNullDate_ReturnsNoContent()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.UpdateProjectJobLetterDateAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>(), (string?)null))
            .ReturnsAsync(true);

        var result = await _controller.UpdateProjectJobMetadataAsync(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateJobMetadataRequest { LetterDate = null });

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region ResetProjectJobAsync

    [Fact]
    public async Task ResetProjectJobAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.ResetProjectJobAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResetProjectJobAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.ResetProjectJobAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(false);

        var result = await _controller.ResetProjectJobAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ResetProjectJobAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.ResetProjectJobAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);

        var result = await _controller.ResetProjectJobAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region DeleteProjectJobAsync

    [Fact]
    public async Task DeleteProjectJobAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.DeleteProjectJobAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteProjectJobAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.DeleteProjectJobAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(false);

        var result = await _controller.DeleteProjectJobAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteProjectJobAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.DeleteProjectJobAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);

        var result = await _controller.DeleteProjectJobAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region AddMemberAsync

    [Fact]
    public async Task AddMemberAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.AddMemberAsync(Guid.NewGuid(), new AddMemberRequest { Email = "member@example.com" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddMemberAsync_WhenEmailMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.AddMemberAsync(Guid.NewGuid(), new AddMemberRequest { Email = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddMemberAsync_WhenEmailIsWhitespace_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.AddMemberAsync(Guid.NewGuid(), new AddMemberRequest { Email = "   " });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddMemberAsync_WhenUserNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.AddMemberByEmailAsync("user-1", projectId, "unknown@example.com"))
            .ReturnsAsync((false, "No registered user found with that email address. They must log in at least once before they can be added."));

        var result = await _controller.AddMemberAsync(projectId, new AddMemberRequest { Email = "unknown@example.com" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddMemberAsync_WhenGenericFailure_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.AddMemberByEmailAsync("user-1", projectId, "self@example.com"))
            .ReturnsAsync((false, "You cannot add yourself as a member — you are already the owner."));

        var result = await _controller.AddMemberAsync(projectId, new AddMemberRequest { Email = "self@example.com" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddMemberAsync_WhenSuccessful_ReturnsOk()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.AddMemberByEmailAsync("user-1", projectId, "member@example.com"))
            .ReturnsAsync((true, (string?)null));

        var result = await _controller.AddMemberAsync(projectId, new AddMemberRequest { Email = "member@example.com" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AddMemberAsync_TrimsEmailWhitespace()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.AddMemberByEmailAsync("user-1", projectId, "member@example.com"))
            .ReturnsAsync((true, (string?)null));

        var result = await _controller.AddMemberAsync(projectId, new AddMemberRequest { Email = "  member@example.com  " });

        Assert.IsType<OkObjectResult>(result);
        _projectServiceMock.Verify(s => s.AddMemberByEmailAsync("user-1", projectId, "member@example.com"), Times.Once);
    }

    #endregion

    #region RemoveMemberAsync

    [Fact]
    public async Task RemoveMemberAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.RemoveMemberAsync(Guid.NewGuid(), new AddMemberRequest { Email = "member@example.com" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenEmailMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");

        var result = await _controller.RemoveMemberAsync(Guid.NewGuid(), new AddMemberRequest { Email = "" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenFails_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.RemoveMemberByEmailAsync("user-1", projectId, "nobody@example.com"))
            .ReturnsAsync((false, "This user is not a member of the project."));

        var result = await _controller.RemoveMemberAsync(projectId, new AddMemberRequest { Email = "nobody@example.com" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RemoveMemberAsync_WhenSuccessful_ReturnsOk()
    {
        SetUserContext("test@example.com", "user-1");
        var projectId = Guid.NewGuid();
        _projectServiceMock.Setup(s => s.RemoveMemberByEmailAsync("user-1", projectId, "member@example.com"))
            .ReturnsAsync((true, (string?)null));

        var result = await _controller.RemoveMemberAsync(projectId, new AddMemberRequest { Email = "member@example.com" });

        Assert.IsType<OkObjectResult>(result);
    }

    #endregion

    #region MoveToStandaloneAsync

    [Fact]
    public async Task MoveToStandaloneAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.MoveToStandaloneAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task MoveToStandaloneAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.MoveJobToStandaloneAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(false);

        var result = await _controller.MoveToStandaloneAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MoveToStandaloneAsync_WhenSuccessful_ReturnsNoContent()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.MoveJobToStandaloneAsync("user-1", It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);

        var result = await _controller.MoveToStandaloneAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region GetProjectJobFileAsync

    [Fact]
    public async Task GetProjectJobFileAsync_WhenUserIdMissing_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", null);

        var result = await _controller.GetProjectJobFileAsync(Guid.NewGuid(), Guid.NewGuid(), "test.jpg");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProjectJobFileAsync_WhenFileNameInvalid_ReturnsBadRequest()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.GetProjectJobFileAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((null, null, "InvalidFileName"));

        var result = await _controller.GetProjectJobFileAsync(Guid.NewGuid(), Guid.NewGuid(), "invalid<file>.jpg");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProjectJobFileAsync_WhenForbidden_ReturnsForbidResult()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.GetProjectJobFileAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((null, null, "Forbidden"));

        var result = await _controller.GetProjectJobFileAsync(Guid.NewGuid(), Guid.NewGuid(), "secret.jpg");

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetProjectJobFileAsync_WhenNotFound_ReturnsNotFound()
    {
        SetUserContext("test@example.com", "user-1");
        _projectServiceMock.Setup(s => s.GetProjectJobFileAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((null, null, "NotFound"));

        var result = await _controller.GetProjectJobFileAsync(Guid.NewGuid(), Guid.NewGuid(), "missing.jpg");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetProjectJobFileAsync_WhenFileExists_ReturnsFileContent()
    {
        SetUserContext("test@example.com", "user-1");
        var fileBytes = new byte[] { 1, 2, 3 };
        _projectServiceMock.Setup(s => s.GetProjectJobFileAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((fileBytes, "image/jpeg", (string?)null));

        var result = await _controller.GetProjectJobFileAsync(Guid.NewGuid(), Guid.NewGuid(), "photo.jpg");

        var fileResult = Assert.IsType<FileContentResult>(result);
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileContents.Should().BeEquivalentTo(fileBytes);
    }

    #endregion
}
