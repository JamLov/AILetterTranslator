using FluentAssertions;
using LetterTranslation.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LetterTranslation.Api.UnitTests.Services;

public class LocalDiskStorageServiceTests : IDisposable
{
    private readonly Mock<ILogger<LocalDiskStorageService>> _loggerMock;
    private readonly string _testBaseDirectory;

    public LocalDiskStorageServiceTests()
    {
        _loggerMock = new Mock<ILogger<LocalDiskStorageService>>();
        // Create a unique temporary directory for each test
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "LetterTranslationTests", Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        // Cleanup after tests
        if (Directory.Exists(_testBaseDirectory))
        {
            Directory.Delete(_testBaseDirectory, true);
        }
    }

    [Fact]
    public async Task EnsureDirectoryAsync_WhenDirectoryDoesNotExist_CreatesDirectory()
    {
        // Arrange
        var service = new LocalDiskStorageService(_loggerMock.Object);
        var testPath = Path.Combine(_testBaseDirectory, "new_folder");

        // Ensure it doesn't exist
        Directory.Exists(testPath).Should().BeFalse();

        // Act
        await service.EnsureDirectoryAsync(testPath);

        // Assert
        Directory.Exists(testPath).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureDirectoryAsync_WhenDirectoryAlreadyExists_DoesNothingAndSucceeds()
    {
        // Arrange
        var service = new LocalDiskStorageService(_loggerMock.Object);
        var testPath = Path.Combine(_testBaseDirectory, "existing_folder");
        
        // Create it beforehand
        Directory.CreateDirectory(testPath);
        Directory.Exists(testPath).Should().BeTrue();

        // Act
        await service.EnsureDirectoryAsync(testPath);

        // Assert
        Directory.Exists(testPath).Should().BeTrue();
        // The fact it didn't throw an exception is the success condition here
    }
}
