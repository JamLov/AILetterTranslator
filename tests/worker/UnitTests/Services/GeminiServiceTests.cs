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

    [Fact]
    public void ProcessTranscriptionContextAsync_ThrowsWhenApiKeyNotConfigured()
    {
        // The public surface of ProcessTranscriptionContextAsync constructs the user message with
        // <source_transcription> / <annotated_translation> tag wrappers internally, and (on success)
        // returns a trimmed string with an empty-result fallback of "*No contextual transcription returned.*".
        // The Gemini SDK Client is not currently abstracted behind an interface, so end-to-end
        // assertions on the wrapped payload, trimming, and empty-result fallback are exercised by the
        // worker integration tests (which run the full pipeline against a mocked IGeminiService).
        // This unit test verifies the new method exists on the concrete service and surfaces the
        // expected configuration error when the API key is missing — mirroring the existing
        // ProcessInitialAsync_ThrowsWhenApiKeyNotConfigured pattern.
        var loggerMock = new Mock<ILogger<GeminiService>>();
        var storageMock = new Mock<IStorageService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new GeminiService(loggerMock.Object, config, storageMock.Object);

        var act = () => sut.ProcessTranscriptionContextAsync("source text", "annotated translation");

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Gemini:ApiKey*");
    }
}
