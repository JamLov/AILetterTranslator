using FluentAssertions;
using LetterTranslation.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace LetterTranslation.Shared.UnitTests.Services;

public class LocalDiskStorageServiceTests : IDisposable
{
    private readonly LocalDiskStorageService _sut;
    private readonly string _testDir;

    public LocalDiskStorageServiceTests()
    {
        _sut = new LocalDiskStorageService(Mock.Of<ILogger<LocalDiskStorageService>>());
        _testDir = Path.Combine(Path.GetTempPath(), "LetterTranslation.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task EnsureDirectoryAsync_CreatesDirectory_WhenNotExists()
    {
        var path = Path.Combine(_testDir, "new-dir");

        await _sut.EnsureDirectoryAsync(path);

        Directory.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureDirectoryAsync_DoesNotThrow_WhenAlreadyExists()
    {
        var path = Path.Combine(_testDir, "existing-dir");
        Directory.CreateDirectory(path);

        var act = () => _sut.EnsureDirectoryAsync(path);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DirectoryExistsAsync_ReturnsTrue_WhenExists()
    {
        var result = await _sut.DirectoryExistsAsync(_testDir);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DirectoryExistsAsync_ReturnsFalse_WhenNotExists()
    {
        var result = await _sut.DirectoryExistsAsync(Path.Combine(_testDir, "nope"));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task WriteTextAsync_And_ReadTextAsync_RoundTrips()
    {
        var path = Path.Combine(_testDir, "test.txt");
        await _sut.WriteTextAsync(path, "hello world");

        var result = await _sut.ReadTextAsync(path);
        result.Should().Be("hello world");
    }

    [Fact]
    public async Task WriteTextAsync_CreatesParentDirectory()
    {
        var path = Path.Combine(_testDir, "sub", "deep", "test.txt");
        await _sut.WriteTextAsync(path, "content");

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task ReadBytesAsync_ReturnsFileContents()
    {
        var path = Path.Combine(_testDir, "binary.bin");
        var expected = new byte[] { 0x01, 0x02, 0x03 };
        await File.WriteAllBytesAsync(path, expected);

        var result = await _sut.ReadBytesAsync(path);
        result.Should().Equal(expected);
    }

    [Fact]
    public async Task FileExistsAsync_ReturnsTrue_WhenExists()
    {
        var path = Path.Combine(_testDir, "exists.txt");
        await File.WriteAllTextAsync(path, "");

        var result = await _sut.FileExistsAsync(path);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task FileExistsAsync_ReturnsFalse_WhenNotExists()
    {
        var result = await _sut.FileExistsAsync(Path.Combine(_testDir, "nope.txt"));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_RemovesFile()
    {
        var path = Path.Combine(_testDir, "to-delete.txt");
        await File.WriteAllTextAsync(path, "delete me");

        await _sut.DeleteFileAsync(path);

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_DoesNotThrow_WhenFileNotExists()
    {
        var act = () => _sut.DeleteFileAsync(Path.Combine(_testDir, "nope.txt"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetDirectoriesAsync_ReturnsSubdirectories()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "a"));
        Directory.CreateDirectory(Path.Combine(_testDir, "b"));

        var result = await _sut.GetDirectoriesAsync(_testDir);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFileNamesAsync_ReturnsFileNames()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "one.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "two.txt"), "");

        var result = await _sut.GetFileNamesAsync(_testDir);
        result.Should().BeEquivalentTo(["one.txt", "two.txt"]);
    }

    [Fact]
    public async Task MoveDirectoryAsync_MovesDirectory()
    {
        var source = Path.Combine(_testDir, "source");
        var dest = Path.Combine(_testDir, "dest");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "content");

        await _sut.MoveDirectoryAsync(source, dest);

        Directory.Exists(source).Should().BeFalse();
        Directory.Exists(dest).Should().BeTrue();
        File.Exists(Path.Combine(dest, "file.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task MoveDirectoryAsync_CreatesParentDirectory()
    {
        var source = Path.Combine(_testDir, "src");
        var dest = Path.Combine(_testDir, "deep", "nested", "dst");
        Directory.CreateDirectory(source);

        await _sut.MoveDirectoryAsync(source, dest);

        Directory.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_RemovesDirectory()
    {
        var path = Path.Combine(_testDir, "to-remove");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "child.txt"), "");

        await _sut.DeleteDirectoryAsync(path);

        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_DoesNotThrow_WhenNotExists()
    {
        var act = () => _sut.DeleteDirectoryAsync(Path.Combine(_testDir, "nope"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteFileAsync_WritesStreamContents()
    {
        var path = Path.Combine(_testDir, "stream.bin");
        var data = new byte[] { 0xAA, 0xBB, 0xCC };
        using var stream = new MemoryStream(data);

        await _sut.WriteFileAsync(path, stream);

        var result = await File.ReadAllBytesAsync(path);
        result.Should().Equal(data);
    }

    [Fact]
    public async Task WriteFileAsync_CreatesParentDirectory()
    {
        var path = Path.Combine(_testDir, "sub", "stream.bin");
        using var stream = new MemoryStream([0x01]);

        await _sut.WriteFileAsync(path, stream);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task CopyFileAsync_CopiesFile()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "content");

        await _sut.CopyFileAsync(source, dest);

        File.Exists(source).Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        (await File.ReadAllTextAsync(dest)).Should().Be("content");
    }

    [Fact]
    public async Task CopyFileAsync_OverwritesExistingDestination()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "dest.txt");
        await File.WriteAllTextAsync(source, "new content");
        await File.WriteAllTextAsync(dest, "old content");

        await _sut.CopyFileAsync(source, dest);

        (await File.ReadAllTextAsync(dest)).Should().Be("new content");
    }

    [Fact]
    public async Task CopyFileAsync_CreatesParentDirectory()
    {
        var source = Path.Combine(_testDir, "source.txt");
        var dest = Path.Combine(_testDir, "nested", "deep", "dest.txt");
        await File.WriteAllTextAsync(source, "content");

        await _sut.CopyFileAsync(source, dest);

        File.Exists(dest).Should().BeTrue();
    }
}
