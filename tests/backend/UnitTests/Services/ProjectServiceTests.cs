using FluentAssertions;
using LetterTranslation.Api.Models;
using LetterTranslation.Api.Services;
using LetterTranslation.Shared.Models;
using LetterTranslation.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using System.Text.Json;
using Xunit;

namespace LetterTranslation.Api.Tests.Services;

public class ProjectServiceTests
{
    private readonly Mock<IStorageService> _storageServiceMock;
    private readonly Mock<IDataService> _dataServiceMock;
    private readonly Mock<ILogger<ProjectService>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly IConfiguration _defaultConfig;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public ProjectServiceTests()
    {
        _storageServiceMock = new Mock<IStorageService>();
        _dataServiceMock = new Mock<IDataService>();
        _loggerMock = new Mock<ILogger<ProjectService>>();
        _timeProvider = new FakeTimeProvider();
        _defaultConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "DataStoragePath", "data" } })
            .Build();
    }

    private ProjectService CreateService(IConfiguration? config = null)
    {
        return new ProjectService(_storageServiceMock.Object, _dataServiceMock.Object, config ?? _defaultConfig, _loggerMock.Object, _timeProvider);
    }

    private string ProjectPath(Guid id) => Path.Combine("data", "projects", id.ToString());
    private string ProjectMetaPath(Guid id) => Path.Combine(ProjectPath(id), "project.json");
    private string ProjectJobsPath(Guid id) => Path.Combine(ProjectPath(id), "jobs");
    private string UserIndexPath(string userId) => Path.Combine("data", "users", userId, "user.json");

    private void SetupProjectMetadata(ProjectMetadata project)
    {
        var json = JsonSerializer.Serialize(project, WriteOptions);
        _storageServiceMock.Setup(s => s.FileExistsAsync(ProjectMetaPath(project.ProjectId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(ProjectMetaPath(project.ProjectId))).ReturnsAsync(json);
    }

    private void SetupUserIndex(string userId, UserIndex index)
    {
        var json = JsonSerializer.Serialize(index, WriteOptions);
        _storageServiceMock.Setup(s => s.FileExistsAsync(UserIndexPath(userId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(UserIndexPath(userId))).ReturnsAsync(json);
    }

    #region CreateProjectAsync

    [Fact]
    public async Task CreateProjectAsync_CreatesDirectoryAndMetadata()
    {
        var service = CreateService();
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("user.json")))).ReturnsAsync(false);

        var request = new CreateProjectRequest { Name = "Test Project", Description = "A description" };

        var result = await service.CreateProjectAsync("user-1", request);

        result.Name.Should().Be("Test Project");
        result.Description.Should().Be("A description");
        result.OwnerUserId.Should().Be("user-1");
        result.ProjectId.Should().NotBe(Guid.Empty);
        result.CreatedAt.Should().Be(new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));

        _storageServiceMock.Verify(s => s.EnsureDirectoryAsync(It.Is<string>(p => p.Contains("jobs"))), Times.Once);
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("project.json")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateProjectAsync_AddsProjectToUserIndex()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.EndsWith("user.json")))).ReturnsAsync(false);

        await service.CreateProjectAsync("user-1", new CreateProjectRequest { Name = "Test" });

        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("user.json")),
            It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region GetProjectsAsync

    [Fact]
    public async Task GetProjectsAsync_WhenNoUserIndex_ReturnsEmpty()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.GetProjectsAsync("user-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsOwnedAndMemberProjects()
    {
        var service = CreateService();
        var ownedId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var userIndex = new UserIndex
        {
            UserId = "user-1",
            ProjectIds = new List<string> { ownedId.ToString(), memberId.ToString() }
        };
        SetupUserIndex("user-1", userIndex);

        var ownedProject = new ProjectMetadata
        {
            ProjectId = ownedId, Name = "Owned", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = new DateTime(2026, 1, 1)
        };
        var memberProject = new ProjectMetadata
        {
            ProjectId = memberId, Name = "Member", OwnerUserId = "other-user",
            MemberUserIds = new List<string> { "user-1" }, CreatedAt = new DateTime(2026, 2, 1)
        };

        SetupProjectMetadata(ownedProject);
        SetupProjectMetadata(memberProject);

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.Is<string>(p => p.Contains("jobs")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(It.Is<string>(p => p.Contains("jobs"))))
            .ReturnsAsync(Array.Empty<string>());

        var result = (await service.GetProjectsAsync("user-1")).ToList();

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Member"); // Newer first
        result[0].IsOwner.Should().BeFalse();
        result[1].Name.Should().Be("Owned");
        result[1].IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task GetProjectsAsync_SkipsInvalidProjectIds()
    {
        var service = CreateService();
        var userIndex = new UserIndex
        {
            UserId = "user-1",
            ProjectIds = new List<string> { "not-a-guid" }
        };
        SetupUserIndex("user-1", userIndex);

        var result = await service.GetProjectsAsync("user-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectsAsync_SkipsProjectsWhereUserIsNeitherOwnerNorMember()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        var userIndex = new UserIndex
        {
            UserId = "user-1",
            ProjectIds = new List<string> { projectId.ToString() }
        };
        SetupUserIndex("user-1", userIndex);

        var project = new ProjectMetadata
        {
            ProjectId = projectId, Name = "Not Mine", OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        };
        SetupProjectMetadata(project);

        var result = await service.GetProjectsAsync("user-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectsAsync_CountsJobs()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        var userIndex = new UserIndex { UserId = "user-1", ProjectIds = new List<string> { projectId.ToString() } };
        SetupUserIndex("user-1", userIndex);

        var project = new ProjectMetadata
        {
            ProjectId = projectId, Name = "With Jobs", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        };
        SetupProjectMetadata(project);

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsPath(projectId)))
            .ReturnsAsync(new[] { "job-1", "job-2", "job-3" });

        var result = (await service.GetProjectsAsync("user-1")).ToList();

        result.Should().HaveCount(1);
        result[0].JobCount.Should().Be(3);
    }

    [Fact]
    public async Task GetProjectsAsync_WhenJobsDirNotExists_ReturnsZeroJobCount()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        var userIndex = new UserIndex { UserId = "user-1", ProjectIds = new List<string> { projectId.ToString() } };
        SetupUserIndex("user-1", userIndex);

        var project = new ProjectMetadata
        {
            ProjectId = projectId, Name = "No Jobs Dir", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        };
        SetupProjectMetadata(project);

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(false);

        var result = (await service.GetProjectsAsync("user-1")).ToList();

        result[0].JobCount.Should().Be(0);
    }

    #endregion

    #region GetProjectDetailAsync

    [Fact]
    public async Task GetProjectDetailAsync_WhenProjectNotFound_ReturnsNull()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.GetProjectDetailAsync("user-1", Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectDetailAsync_WhenUserNotOwnerOrMember_ReturnsNull()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Secret", OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.GetProjectDetailAsync("user-1", projectId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectDetailAsync_AsOwner_ReturnsDetail()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "My Project", OwnerUserId = "user-1",
            MemberUserIds = new List<string> { "member-1" }, CreatedAt = new DateTime(2026, 3, 1)
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(false);
        _dataServiceMock.Setup(s => s.GetUserEmailAsync("member-1")).ReturnsAsync("member@example.com");

        var result = await service.GetProjectDetailAsync("user-1", projectId);

        result.Should().NotBeNull();
        result!.IsOwner.Should().BeTrue();
        result.MemberEmails.Should().Contain("member@example.com");
        result.MemberCount.Should().Be(1);
        result.Metadata.OwnerUserId.Should().BeEmpty(); // Stripped for client
        result.Metadata.MemberUserIds.Should().BeEmpty(); // Stripped for client
    }

    [Fact]
    public async Task GetProjectDetailAsync_AsMember_ReturnsDetail()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Shared Project", OwnerUserId = "owner",
            MemberUserIds = new List<string> { "user-1" }, CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(false);

        var result = await service.GetProjectDetailAsync("user-1", projectId);

        result.Should().NotBeNull();
        result!.IsOwner.Should().BeFalse();
    }

    [Fact]
    public async Task GetProjectDetailAsync_WithJobs_ReturnsJobsSortedByDate()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Project", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsPath(projectId)))
            .ReturnsAsync(new[] { Path.Combine(ProjectJobsPath(projectId), "job-a"), Path.Combine(ProjectJobsPath(projectId), "job-b") });

        var metaA = new JobMetadata { JobId = Guid.NewGuid(), JobName = "Older", CreatedAt = new DateTime(2026, 1, 1), Status = "Finished" };
        var metaB = new JobMetadata { JobId = Guid.NewGuid(), JobName = "Newer", CreatedAt = new DateTime(2026, 3, 1), Status = "Not Started" };

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("job-a") && p.EndsWith("metadata.json")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("job-b") && p.EndsWith("metadata.json")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("job-a") && p.EndsWith("metadata.json"))))
            .ReturnsAsync(JsonSerializer.Serialize(metaA));
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("job-b") && p.EndsWith("metadata.json"))))
            .ReturnsAsync(JsonSerializer.Serialize(metaB));

        var result = await service.GetProjectDetailAsync("user-1", projectId);

        result!.Jobs.Should().HaveCount(2);
        result.Jobs[0].JobName.Should().Be("Newer");
        result.Jobs[1].JobName.Should().Be("Older");
    }

    [Fact]
    public async Task GetProjectDetailAsync_SkipsCorruptedJobMetadata()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Project", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsPath(projectId)))
            .ReturnsAsync(new[] { Path.Combine(ProjectJobsPath(projectId), "bad-job") });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("bad-job") && p.EndsWith("metadata.json")))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(It.Is<string>(p => p.Contains("bad-job"))))
            .ThrowsAsync(new JsonException("corrupt"));

        var result = await service.GetProjectDetailAsync("user-1", projectId);

        result!.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectDetailAsync_WhenMemberEmailNotFound_OmitsFromList()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Project", OwnerUserId = "user-1",
            MemberUserIds = new List<string> { "ghost-user" }, CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(false);
        _dataServiceMock.Setup(s => s.GetUserEmailAsync("ghost-user")).ReturnsAsync((string?)null);

        var result = await service.GetProjectDetailAsync("user-1", projectId);

        result!.MemberEmails.Should().BeEmpty();
        result.MemberCount.Should().Be(1); // Still counts the member
    }

    #endregion

    #region UpdateProjectAsync

    [Fact]
    public async Task UpdateProjectAsync_WhenProjectNotFound_ReturnsNull()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.UpdateProjectAsync("user-1", Guid.NewGuid(), new UpdateProjectRequest { Name = "New" });

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProjectAsync_WhenNotOwner_ReturnsNull()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Old", OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.UpdateProjectAsync("user-1", projectId, new UpdateProjectRequest { Name = "New" });

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProjectAsync_UpdatesNameAndDescription()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Old Name", Description = "Old Desc", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.UpdateProjectAsync("user-1", projectId, new UpdateProjectRequest
        {
            Name = "New Name",
            Description = "New Desc"
        });

        result.Should().NotBeNull();
        result!.Name.Should().Be("New Name");
        result.Description.Should().Be("New Desc");
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            ProjectMetaPath(projectId), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProjectAsync_WithNullFields_DoesNotOverwrite()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, Name = "Keep Me", Description = "Keep Me Too", OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.UpdateProjectAsync("user-1", projectId, new UpdateProjectRequest
        {
            Name = null,
            Description = null
        });

        result!.Name.Should().Be("Keep Me");
        result.Description.Should().Be("Keep Me Too");
    }

    #endregion

    #region DeleteProjectAsync

    [Fact]
    public async Task DeleteProjectAsync_WhenProjectNotFound_ReturnsFalse()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.DeleteProjectAsync("user-1", Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenNotOwner_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.DeleteProjectAsync("user-1", projectId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenProjectHasJobs_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsPath(projectId)))
            .ReturnsAsync(new[] { "some-job" });

        var result = await service.DeleteProjectAsync("user-1", projectId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProjectAsync_WhenEmpty_DeletesAndRemovesFromIndices()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string> { "member-1" }, CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(ProjectJobsPath(projectId))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.GetDirectoriesAsync(ProjectJobsPath(projectId)))
            .ReturnsAsync(Array.Empty<string>());

        // Both user indices exist
        SetupUserIndex("member-1", new UserIndex { UserId = "member-1", ProjectIds = new List<string> { projectId.ToString() } });
        SetupUserIndex("user-1", new UserIndex { UserId = "user-1", ProjectIds = new List<string> { projectId.ToString() } });

        var result = await service.DeleteProjectAsync("user-1", projectId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.DeleteDirectoryAsync(ProjectPath(projectId)), Times.Once);
        // Should write updated indices for both owner and member
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("user.json")), It.IsAny<string>()), Times.AtLeast(2));
    }

    #endregion

    #region CreateProjectJobAsync

    [Fact]
    public async Task CreateProjectJobAsync_WhenNotOwner_ThrowsUnauthorized()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        var request = new CreateJobRequest { JobName = "Test", Files = new List<IFormFile> { mockFile.Object } };

        var act = () => service.CreateProjectJobAsync("user-1", projectId, request);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task CreateProjectJobAsync_WhenProjectNotFound_ThrowsUnauthorized()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        var act = () => service.CreateProjectJobAsync("user-1", Guid.NewGuid(),
            new CreateJobRequest { JobName = "Test", Files = new List<IFormFile> { mockFile.Object } });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task CreateProjectJobAsync_AsOwner_CreatesJobWithMetadata()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("letter.jpg");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1, 2, 3 }));

        var request = new CreateJobRequest
        {
            JobName = "Project Job",
            Notes = "Some notes",
            Files = new List<IFormFile> { mockFile.Object }
        };

        var result = await service.CreateProjectJobAsync("user-1", projectId, request);

        result.JobName.Should().Be("Project Job");
        result.Status.Should().Be("Not Started");
        result.OriginalFileCount.Should().Be(1);
        result.CreatedByUserId.Should().Be("user-1");
        result.CreatedAt.Should().Be(new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));

        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("notes.txt")), "Some notes"), Times.Once);
        _storageServiceMock.Verify(s => s.WriteFileAsync(
            It.Is<string>(p => p.EndsWith("letter.jpg")), It.IsAny<Stream>()), Times.Once);
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("metadata.json")), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateProjectJobAsync_WithNoNotes_DoesNotWriteNotesFile()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("test.jpg");
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        await service.CreateProjectJobAsync("user-1", projectId,
            new CreateJobRequest { JobName = "No Notes", Files = new List<IFormFile> { mockFile.Object } });

        _storageServiceMock.Verify(s => s.WriteTextAsync(
            It.Is<string>(p => p.EndsWith("notes.txt")), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region GetProjectJobDetailAsync

    [Fact]
    public async Task GetProjectJobDetailAsync_WhenProjectNotFound_ReturnsNull()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.GetProjectJobDetailAsync("user-1", Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectJobDetailAsync_WhenNotMemberOrOwner_ReturnsNull()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.GetProjectJobDetailAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectJobDetailAsync_WhenJobDirNotFound_ReturnsNull()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.Is<string>(p => p.Contains("jobs") && !p.EndsWith("jobs"))))
            .ReturnsAsync(false);

        var result = await service.GetProjectJobDetailAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectJobDetailAsync_WithCompleteJob_ReturnsDetail()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine(ProjectJobsPath(projectId), jobId.ToString());

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Complete", CreatedAt = DateTime.UtcNow, Status = "Finished", OriginalFileCount = 1
        };

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(Path.Combine(jobPath, "files"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "metadata.json"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "metadata.json")))
            .ReturnsAsync(JsonSerializer.Serialize(metadata));
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "notes.txt"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "notes.txt"))).ReturnsAsync("Job notes");
        _storageServiceMock.Setup(s => s.GetFileNamesAsync(Path.Combine(jobPath, "files")))
            .ReturnsAsync(new[] { "img.jpg" });
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed.md"))).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(Path.Combine(jobPath, "Transcribed.md"))).ReturnsAsync("# Hello");
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_Translated.md"))).ReturnsAsync(false);
        _storageServiceMock.Setup(s => s.FileExistsAsync(Path.Combine(jobPath, "Transcribed_Translated_With_Notes.md"))).ReturnsAsync(false);

        var result = await service.GetProjectJobDetailAsync("user-1", projectId, jobId);

        result.Should().NotBeNull();
        result!.Metadata.JobId.Should().Be(jobId);
        result.Notes.Should().Be("Job notes");
        result.OriginalFileNames.Should().Contain("img.jpg");
        result.TranscribedHtml.Should().Contain("Hello");
        result.TranslatedHtml.Should().BeNull();
    }

    #endregion

    #region ResetProjectJobAsync

    [Fact]
    public async Task ResetProjectJobAsync_WhenNotOwner_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.ResetProjectJobAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetProjectJobAsync_WhenMetadataNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("jobs") && p.EndsWith("metadata.json"))))
            .ReturnsAsync(false);

        var result = await service.ResetProjectJobAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetProjectJobAsync_ResetsStatusAndDeletesOutputFiles()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine(ProjectJobsPath(projectId), jobId.ToString());
        var metadataPath = Path.Combine(jobPath, "metadata.json");

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var metadata = new JobMetadata
        {
            JobId = jobId, JobName = "Done", Status = "Finished", ErrorMessage = "old error", CreatedAt = DateTime.UtcNow
        };
        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath)).ReturnsAsync(JsonSerializer.Serialize(metadata));

        var result = await service.ResetProjectJobAsync("user-1", projectId, jobId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobPath, "Transcribed.md")), Times.Once);
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobPath, "Transcribed_Translated.md")), Times.Once);
        _storageServiceMock.Verify(s => s.DeleteFileAsync(Path.Combine(jobPath, "Transcribed_Translated_With_Notes.md")), Times.Once);
        _storageServiceMock.Verify(s => s.WriteTextAsync(metadataPath,
            It.Is<string>(j => j.Contains("\"Status\": \"Not Started\""))), Times.Once);
    }

    #endregion

    #region DeleteProjectJobAsync

    [Fact]
    public async Task DeleteProjectJobAsync_WhenNotOwner_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.DeleteProjectJobAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProjectJobAsync_WhenJobDirNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.Is<string>(p => !p.EndsWith("projects"))))
            .ReturnsAsync(false);

        var result = await service.DeleteProjectJobAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProjectJobAsync_WhenSuccessful_DeletesDirectory()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine(ProjectJobsPath(projectId), jobId.ToString());

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(jobPath)).ReturnsAsync(true);

        var result = await service.DeleteProjectJobAsync("user-1", projectId, jobId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.DeleteDirectoryAsync(jobPath), Times.Once);
    }

    #endregion

    #region UpdateProjectJobLetterDateAsync

    [Fact]
    public async Task UpdateProjectJobLetterDateAsync_WhenProjectNotFound_ReturnsFalse()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.UpdateProjectJobLetterDateAsync("user-1", Guid.NewGuid(), Guid.NewGuid(), "2026-01-15");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProjectJobLetterDateAsync_WhenNotMemberOrOwner_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.UpdateProjectJobLetterDateAsync("user-1", projectId, Guid.NewGuid(), "2026-01-15");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProjectJobLetterDateAsync_WhenMetadataNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.FileExistsAsync(It.Is<string>(p => p.Contains("jobs") && p.EndsWith("metadata.json"))))
            .ReturnsAsync(false);

        var result = await service.UpdateProjectJobLetterDateAsync("user-1", projectId, Guid.NewGuid(), "2026-01-15");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProjectJobLetterDateAsync_UpdatesDate()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var metadataPath = Path.Combine(ProjectJobsPath(projectId), jobId.ToString(), "metadata.json");

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var metadata = new JobMetadata { JobId = jobId, JobName = "Test", Status = "Finished", CreatedAt = DateTime.UtcNow };
        _storageServiceMock.Setup(s => s.FileExistsAsync(metadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(metadataPath)).ReturnsAsync(JsonSerializer.Serialize(metadata));

        var result = await service.UpdateProjectJobLetterDateAsync("user-1", projectId, jobId, "2026-01-15");

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.WriteTextAsync(metadataPath,
            It.Is<string>(j => j.Contains("2026-01-15"))), Times.Once);
    }

    #endregion

    #region MoveJobToProjectAsync

    [Fact]
    public async Task MoveJobToProjectAsync_WhenProjectNotFoundOrNotOwner_ReturnsFalse()
    {
        var service = CreateService();
        _storageServiceMock.Setup(s => s.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var result = await service.MoveJobToProjectAsync("user-1", Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveJobToProjectAsync_WhenSourceNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.Is<string>(p => p.Contains("users"))))
            .ReturnsAsync(false);

        var result = await service.MoveJobToProjectAsync("user-1", Guid.NewGuid(), projectId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveJobToProjectAsync_MovesDirectoryAndUpdatesMetadata()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var sourcePath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());
        var destPath = Path.Combine(ProjectJobsPath(projectId), jobId.ToString());
        var destMetadataPath = Path.Combine(destPath, "metadata.json");

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(sourcePath)).ReturnsAsync(true);

        var metadata = new JobMetadata { JobId = jobId, JobName = "Moving", Status = "Finished", CreatedAt = DateTime.UtcNow };
        _storageServiceMock.Setup(s => s.FileExistsAsync(destMetadataPath)).ReturnsAsync(true);
        _storageServiceMock.Setup(s => s.ReadTextAsync(destMetadataPath)).ReturnsAsync(JsonSerializer.Serialize(metadata));

        var result = await service.MoveJobToProjectAsync("user-1", jobId, projectId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.MoveDirectoryAsync(sourcePath, destPath), Times.Once);
        _storageServiceMock.Verify(s => s.WriteTextAsync(destMetadataPath,
            It.Is<string>(j => j.Contains("user-1"))), Times.Once);
    }

    #endregion

    #region MoveJobToStandaloneAsync

    [Fact]
    public async Task MoveJobToStandaloneAsync_WhenNotOwner_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var result = await service.MoveJobToStandaloneAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveJobToStandaloneAsync_WhenSourceNotFound_ReturnsFalse()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(It.Is<string>(p => p.Contains("jobs") && !p.EndsWith("jobs"))))
            .ReturnsAsync(false);

        var result = await service.MoveJobToStandaloneAsync("user-1", projectId, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveJobToStandaloneAsync_MovesDirectory()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var sourcePath = Path.Combine(ProjectJobsPath(projectId), jobId.ToString());
        var destPath = Path.Combine("data", "users", "user-1", "jobs", jobId.ToString());

        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _storageServiceMock.Setup(s => s.DirectoryExistsAsync(sourcePath)).ReturnsAsync(true);

        var result = await service.MoveJobToStandaloneAsync("user-1", projectId, jobId);

        result.Should().BeTrue();
        _storageServiceMock.Verify(s => s.MoveDirectoryAsync(sourcePath, destPath), Times.Once);
    }

    #endregion

    #region AddMemberByEmailAsync

    [Fact]
    public async Task AddMemberByEmailAsync_WhenNotOwner_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var (success, error) = await service.AddMemberByEmailAsync("user-1", projectId, "someone@example.com");

        success.Should().BeFalse();
        error.Should().Contain("not the owner");
    }

    [Fact]
    public async Task AddMemberByEmailAsync_WhenUserNotRegistered_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("unknown@example.com")).ReturnsAsync((string?)null);

        var (success, error) = await service.AddMemberByEmailAsync("user-1", projectId, "unknown@example.com");

        success.Should().BeFalse();
        error.Should().Contain("No registered user");
    }

    [Fact]
    public async Task AddMemberByEmailAsync_WhenAddingSelf_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("me@example.com")).ReturnsAsync("user-1");

        var (success, error) = await service.AddMemberByEmailAsync("user-1", projectId, "me@example.com");

        success.Should().BeFalse();
        error.Should().Contain("cannot add yourself");
    }

    [Fact]
    public async Task AddMemberByEmailAsync_WhenAlreadyMember_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string> { "member-1" }, CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("member@example.com")).ReturnsAsync("member-1");

        var (success, error) = await service.AddMemberByEmailAsync("user-1", projectId, "member@example.com");

        success.Should().BeFalse();
        error.Should().Contain("already a member");
    }

    [Fact]
    public async Task AddMemberByEmailAsync_Success_UpdatesMetadataAndIndex()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("new@example.com")).ReturnsAsync("new-user");
        _storageServiceMock.Setup(s => s.FileExistsAsync(UserIndexPath("new-user"))).ReturnsAsync(false);

        var (success, error) = await service.AddMemberByEmailAsync("user-1", projectId, "new@example.com");

        success.Should().BeTrue();
        error.Should().BeNull();
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            ProjectMetaPath(projectId), It.Is<string>(j => j.Contains("new-user"))), Times.Once);
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            UserIndexPath("new-user"), It.Is<string>(j => j.Contains(projectId.ToString()))), Times.Once);
    }

    #endregion

    #region RemoveMemberByEmailAsync

    [Fact]
    public async Task RemoveMemberByEmailAsync_WhenNotOwner_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "other",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        var (success, error) = await service.RemoveMemberByEmailAsync("user-1", projectId, "someone@example.com");

        success.Should().BeFalse();
        error.Should().Contain("not the owner");
    }

    [Fact]
    public async Task RemoveMemberByEmailAsync_WhenUserNotMember_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("nobody@example.com")).ReturnsAsync("nobody-id");

        var (success, error) = await service.RemoveMemberByEmailAsync("user-1", projectId, "nobody@example.com");

        success.Should().BeFalse();
        error.Should().Contain("not a member");
    }

    [Fact]
    public async Task RemoveMemberByEmailAsync_WhenEmailNotFound_ReturnsFailure()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string>(), CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("ghost@example.com")).ReturnsAsync((string?)null);

        var (success, error) = await service.RemoveMemberByEmailAsync("user-1", projectId, "ghost@example.com");

        success.Should().BeFalse();
        error.Should().Contain("not a member");
    }

    [Fact]
    public async Task RemoveMemberByEmailAsync_Success_UpdatesMetadataAndIndex()
    {
        var service = CreateService();
        var projectId = Guid.NewGuid();
        SetupProjectMetadata(new ProjectMetadata
        {
            ProjectId = projectId, OwnerUserId = "user-1",
            MemberUserIds = new List<string> { "member-1" }, CreatedAt = DateTime.UtcNow
        });

        _dataServiceMock.Setup(s => s.FindUserIdByEmailAsync("member@example.com")).ReturnsAsync("member-1");
        SetupUserIndex("member-1", new UserIndex { UserId = "member-1", ProjectIds = new List<string> { projectId.ToString() } });

        var (success, error) = await service.RemoveMemberByEmailAsync("user-1", projectId, "member@example.com");

        success.Should().BeTrue();
        error.Should().BeNull();
        _storageServiceMock.Verify(s => s.WriteTextAsync(
            ProjectMetaPath(projectId), It.Is<string>(j => !j.Contains("member-1"))), Times.Once);
    }

    #endregion
}
