using FluentAssertions;
using LetterTranslation.Shared.Services;
using LetterTranslation.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LetterTranslation.Worker.UnitTests.Services;

public class GeminiServiceTests
{
    [Fact]
    public void ProcessInitialAsync_ThrowsWhenApiKeyNotConfigured()
    {
        var loggerMock = new Mock<ILogger<GeminiService>>();
        var storageMock = new Mock<IStorageService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new GeminiService(loggerMock.Object, config, storageMock.Object);

        var act = () => sut.ProcessInitialAsync(new[] { "image.jpg" }, null);

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Gemini:ApiKey*");
    }
}
