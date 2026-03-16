using FluentAssertions;
using LetterTranslation.Api.Controllers;
using LetterTranslation.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace LetterTranslation.Api.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<IDataService> _dataServiceMock;
    private readonly Mock<ILogger<AuthController>> _loggerMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _userServiceMock = new Mock<IUserService>();
        _dataServiceMock = new Mock<IDataService>();
        _loggerMock = new Mock<ILogger<AuthController>>();

        _controller = new AuthController(_userServiceMock.Object, _dataServiceMock.Object, _loggerMock.Object);
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
    public async Task LoginAsync_WhenUserIsNotAllowed_ReturnsForbidden()
    {
        // Arrange
        SetUserContext("unauthorized@example.com", "12345");
        _userServiceMock.Setup(s => s.IsUserAllowed("unauthorized@example.com")).Returns(false);

        // Act
        var result = await _controller.LoginAsync();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        
        // Ensure data service wasn't called
        _dataServiceMock.Verify(s => s.InitializeUserWorkspaceAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenUserIdIsMissing_ReturnsBadRequest()
    {
        // Arrange
        SetUserContext("authorized@example.com", ""); // Missing ID
        _userServiceMock.Setup(s => s.IsUserAllowed("authorized@example.com")).Returns(true);

        // Act
        var result = await _controller.LoginAsync();

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        
        // Ensure data service wasn't called
        _dataServiceMock.Verify(s => s.InitializeUserWorkspaceAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenUserIsAllowedAndHasId_InitializesWorkspaceAndReturnsOk()
    {
        // Arrange
        var email = "authorized@example.com";
        var userId = "google-user-123";
        SetUserContext(email, userId);
        _userServiceMock.Setup(s => s.IsUserAllowed(email)).Returns(true);
        _dataServiceMock.Setup(s => s.InitializeUserWorkspaceAsync(userId, It.IsAny<string?>())).Returns(Task.CompletedTask);

        // Act
        var result = await _controller.LoginAsync();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        
        // Verify the workspace was initialized
        _dataServiceMock.Verify(s => s.InitializeUserWorkspaceAsync(userId, email), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenDataServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        var email = "authorized@example.com";
        var userId = "google-user-123";
        SetUserContext(email, userId);
        _userServiceMock.Setup(s => s.IsUserAllowed(email)).Returns(true);
        
        _dataServiceMock.Setup(s => s.InitializeUserWorkspaceAsync(userId, It.IsAny<string?>()))
            .ThrowsAsync(new Exception("Disk full"));

        // Act
        var result = await _controller.LoginAsync();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void GetSecureData_WhenUserIsNotAllowed_ReturnsForbidden()
    {
        // Arrange
        SetUserContext("unauthorized@example.com", "123");
        _userServiceMock.Setup(s => s.IsUserAllowed(It.IsAny<string>())).Returns(false);

        // Act
        var result = _controller.GetSecureData();

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void GetSecureData_WhenUserIsAllowed_ReturnsOk()
    {
        // Arrange
        SetUserContext("authorized@example.com", "123");
        _userServiceMock.Setup(s => s.IsUserAllowed(It.IsAny<string>())).Returns(true);

        // Act
        var result = _controller.GetSecureData();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
