using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LetterTranslation.Api.Models;
using LetterTranslation.Shared.Models;

namespace LetterTranslation.Api.IntegrationTests;

public class JobsControllerTests : IAsyncLifetime
{
    private readonly IsolatedTestWebAppFactory _factory = new();
    private HttpClient _client = null!;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.Client;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
    
    private async Task<JobMetadata> CreateTestJob(string jobName = "Test Job", string notes = "Some notes", string fileName = "test.jpg")
    {
        await using var stream = new MemoryStream("This is a dummy file."u8.ToArray());
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        using var content = new MultipartFormDataContent
        {
            { new StringContent(jobName), "JobName" },
            { new StringContent(notes), "Notes" },
            { fileContent, "Files", fileName }
        };

        var response = await _client.PostAsync("/api/jobs", content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JobMetadata>(json, _jsonSerializerOptions)!;
    }

    [Fact]
    public async Task CreateJob_WithValidData_ReturnsCreatedAndCreatesJobDirectory()
    {
        // Act
        var jobMetadata = await CreateTestJob();

        // Assert
        Assert.NotNull(jobMetadata);

        // Verify that the directory and files were created in the temp location
        var subject = _factory.Configuration.GetSection("TestUser")["Subject"];
        var expectedJobPath = Path.Combine(_factory.TestRootPath, subject, "data", jobMetadata.JobId.ToString());
        
        Assert.True(Directory.Exists(expectedJobPath));
        Assert.True(File.Exists(Path.Combine(expectedJobPath, "metadata.json")));
        Assert.True(File.Exists(Path.Combine(expectedJobPath, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(expectedJobPath, "files", "test.jpg")));
    }

    [Fact]
    public async Task GetJobs_AfterCreatingOneJob_ReturnsJobInList()
    {
        // Arrange
        var createdJob = await CreateTestJob("My First Job");

        // Act
        var response = await _client.GetAsync("/api/jobs");
        var json = await response.Content.ReadAsStringAsync();
        var jobs = JsonSerializer.Deserialize<List<JobMetadata>>(json, _jsonSerializerOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(jobs);
        var jobInList = Assert.Single(jobs);
        Assert.Equal(createdJob.JobId, jobInList.JobId);
        Assert.Equal("My First Job", jobInList.JobName);
    }

    [Fact]
    public async Task GetJobDetail_AfterCreatingJob_ReturnsCorrectDetails()
    {
        // Arrange
        var createdJob = await CreateTestJob("My Second Job", "These are the notes.", "page1.jpg");

        // Act
        var response = await _client.GetAsync($"/api/jobs/{createdJob.JobId}");
        var json = await response.Content.ReadAsStringAsync();
        var jobDetail = JsonSerializer.Deserialize<JobDetail>(json, _jsonSerializerOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(jobDetail);
        Assert.Equal(createdJob.JobId, jobDetail.Metadata.JobId);
        Assert.Equal("My Second Job", jobDetail.Metadata.JobName);
        Assert.Equal("These are the notes.", jobDetail.Notes);
        var fileName = Assert.Single(jobDetail.OriginalFileNames);
        Assert.Equal("page1.jpg", fileName);
    }
}
