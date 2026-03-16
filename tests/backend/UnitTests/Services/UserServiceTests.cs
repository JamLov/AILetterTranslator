using FluentAssertions;
using LetterTranslation.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LetterTranslation.Api.UnitTests.Services;

public class UserServiceTests
{
    private readonly Mock<ILogger<UserService>> _loggerMock;

    public UserServiceTests()
    {
        _loggerMock = new Mock<ILogger<UserService>>();
    }

    private IConfiguration CreateConfiguration(string[] allowedUsers)
    {
        var inMemorySettings = new Dictionary<string, string?>();
        for (int i = 0; i < allowedUsers.Length; i++)
        {
            inMemorySettings[$"AllowedUsers:{i}"] = allowedUsers[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public void IsUserAllowed_WhenEmailIsInAllowedUsersList_ReturnsTrue()
    {
        // Arrange
        var config = CreateConfiguration(new[] { "test@example.com", "another@example.com" });
        var service = new UserService(config, _loggerMock.Object);

        // Act
        var result = service.IsUserAllowed("test@example.com");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsUserAllowed_WhenEmailIsInAllowedUsersListDifferentCase_ReturnsTrue()
    {
        // Arrange
        var config = CreateConfiguration(new[] { "TEST@EXAMPLE.COM" });
        var service = new UserService(config, _loggerMock.Object);

        // Act
        var result = service.IsUserAllowed("test@example.com");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsUserAllowed_WhenEmailIsNotInAllowedUsersList_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfiguration(new[] { "test@example.com" });
        var service = new UserService(config, _loggerMock.Object);

        // Act
        var result = service.IsUserAllowed("unauthorized@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void IsUserAllowed_WhenEmailIsNullOrEmpty_ReturnsFalse(string? invalidEmail)
    {
        // Arrange
        var config = CreateConfiguration(new[] { "test@example.com" });
        var service = new UserService(config, _loggerMock.Object);

        // Act
        var result = service.IsUserAllowed(invalidEmail);

        // Assert
        result.Should().BeFalse();
    }
}
