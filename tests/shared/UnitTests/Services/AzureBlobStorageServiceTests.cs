using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using LetterTranslation.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LetterTranslation.Shared.UnitTests.Services;

public class AzureBlobStorageServiceTests
{
    private readonly Mock<BlobContainerClient> _containerMock = new();
    private readonly AzureBlobStorageService _sut;

    public AzureBlobStorageServiceTests()
    {
        _sut = new AzureBlobStorageService(
            Mock.Of<ILogger<AzureBlobStorageService>>(),
            _containerMock.Object);
    }

    // --- Constructor tests ---

    [Fact]
    public void Constructor_Throws_WhenConnectionStringMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => new AzureBlobStorageService(
            Mock.Of<ILogger<AzureBlobStorageService>>(), config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*not configured*");
    }

    // --- EnsureDirectoryAsync ---

    [Fact]
    public async Task EnsureDirectoryAsync_IsNoOp()
    {
        await _sut.EnsureDirectoryAsync("some/path");
        // No exception, no calls to container
        _containerMock.VerifyNoOtherCalls();
    }

    // --- DirectoryExistsAsync ---

    [Fact]
    public async Task DirectoryExistsAsync_ReturnsTrue_WhenBlobsExist()
    {
        var page = Page<BlobItem>.FromValues(
            new[] { BlobsModelFactory.BlobItem("test/path/file.txt") },
            continuationToken: null,
            Mock.Of<Response>());

        var pageable = AsyncPageable<BlobItem>.FromPages(new[] { page });

        _containerMock.Setup(c => c.GetBlobsAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                It.Is<string>(p => p == "test/path/"), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var result = await _sut.DirectoryExistsAsync("test/path");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DirectoryExistsAsync_ReturnsFalse_WhenNoBlobsExist()
    {
        var pageable = AsyncPageable<BlobItem>.FromPages(
            Array.Empty<Page<BlobItem>>());

        _containerMock.Setup(c => c.GetBlobsAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                It.Is<string>(p => p == "test/path/"), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var result = await _sut.DirectoryExistsAsync("test/path");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DirectoryExistsAsync_NormalizesBackslashesAndLeadingSlash()
    {
        var pageable = AsyncPageable<BlobItem>.FromPages(
            Array.Empty<Page<BlobItem>>());

        _containerMock.Setup(c => c.GetBlobsAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                It.Is<string>(p => p == "some/nested/path/"), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        await _sut.DirectoryExistsAsync(@"\some\nested\path");

        _containerMock.Verify(c => c.GetBlobsAsync(
            It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
            "some/nested/path/", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- GetDirectoriesAsync ---

    [Fact]
    public async Task GetDirectoriesAsync_ReturnsPrefixes_WithoutTrailingSlash()
    {
        var page = Page<BlobHierarchyItem>.FromValues(
            new[]
            {
                BlobsModelFactory.BlobHierarchyItem("dir1/", null),
                BlobsModelFactory.BlobHierarchyItem("dir2/", null),
            },
            continuationToken: null,
            Mock.Of<Response>());

        var pageable = AsyncPageable<BlobHierarchyItem>.FromPages(new[] { page });

        _containerMock.Setup(c => c.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                "/", "parent/", It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var result = await _sut.GetDirectoriesAsync("parent");

        result.Should().BeEquivalentTo(new[] { "dir1", "dir2" });
    }

    // --- GetFileNamesAsync ---

    [Fact]
    public async Task GetFileNamesAsync_ReturnsJustFileNames()
    {
        var page = Page<BlobHierarchyItem>.FromValues(
            new[]
            {
                BlobsModelFactory.BlobHierarchyItem(null, BlobsModelFactory.BlobItem("parent/file1.txt")),
                BlobsModelFactory.BlobHierarchyItem(null, BlobsModelFactory.BlobItem("parent/file2.jpg")),
            },
            continuationToken: null,
            Mock.Of<Response>());

        var pageable = AsyncPageable<BlobHierarchyItem>.FromPages(new[] { page });

        _containerMock.Setup(c => c.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                "/", "parent/", It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var result = await _sut.GetFileNamesAsync("parent");

        result.Should().BeEquivalentTo(new[] { "file1.txt", "file2.jpg" });
    }

    [Fact]
    public async Task GetFileNamesAsync_HandlesFileWithNoSlash()
    {
        var page = Page<BlobHierarchyItem>.FromValues(
            new[]
            {
                BlobsModelFactory.BlobHierarchyItem(null, BlobsModelFactory.BlobItem("rootfile.txt")),
            },
            continuationToken: null,
            Mock.Of<Response>());

        var pageable = AsyncPageable<BlobHierarchyItem>.FromPages(new[] { page });

        _containerMock.Setup(c => c.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                "/", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var result = await _sut.GetFileNamesAsync("/");

        result.Should().Contain("rootfile.txt");
    }

    // --- FileExistsAsync ---

    [Fact]
    public async Task FileExistsAsync_ReturnsTrue_WhenBlobExists()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _containerMock.Setup(c => c.GetBlobClient("some/file.txt"))
            .Returns(blobMock.Object);

        var result = await _sut.FileExistsAsync("some/file.txt");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task FileExistsAsync_ReturnsFalse_WhenBlobDoesNotExist()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        _containerMock.Setup(c => c.GetBlobClient("some/file.txt"))
            .Returns(blobMock.Object);

        var result = await _sut.FileExistsAsync("some/file.txt");

        result.Should().BeFalse();
    }

    // --- ReadTextAsync ---

    [Fact]
    public async Task ReadTextAsync_ReturnsContent()
    {
        var blobMock = new Mock<BlobClient>();
        var content = "hello world"u8.ToArray();
        blobMock.Setup(b => b.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, CancellationToken _) =>
            {
                stream.Write(content);
                return Task.FromResult(Mock.Of<Response>());
            });

        _containerMock.Setup(c => c.GetBlobClient("test/file.txt"))
            .Returns(blobMock.Object);

        var result = await _sut.ReadTextAsync("test/file.txt");

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task ReadTextAsync_ReturnsEmpty_WhenBlobIsEmpty()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Response>()));

        _containerMock.Setup(c => c.GetBlobClient("test/empty.txt"))
            .Returns(blobMock.Object);

        var result = await _sut.ReadTextAsync("test/empty.txt");

        result.Should().BeEmpty();
    }

    // --- ReadBytesAsync ---

    [Fact]
    public async Task ReadBytesAsync_ReturnsByteArray()
    {
        var blobMock = new Mock<BlobClient>();
        var expected = new byte[] { 0x01, 0x02, 0x03 };
        blobMock.Setup(b => b.DownloadToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, CancellationToken _) =>
            {
                stream.Write(expected);
                return Task.FromResult(Mock.Of<Response>());
            });

        _containerMock.Setup(c => c.GetBlobClient("test/data.bin"))
            .Returns(blobMock.Object);

        var result = await _sut.ReadBytesAsync("test/data.bin");

        result.Should().Equal(expected);
    }

    // --- WriteTextAsync ---

    [Fact]
    public async Task WriteTextAsync_UploadsContent()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        _containerMock.Setup(c => c.GetBlobClient("out/file.txt"))
            .Returns(blobMock.Object);

        await _sut.WriteTextAsync("out/file.txt", "content");

        blobMock.Verify(b => b.UploadAsync(
            It.Is<BinaryData>(d => d.ToString() == "content"),
            true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- WriteFileAsync ---

    [Fact]
    public async Task WriteFileAsync_UploadsStream()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.UploadAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        _containerMock.Setup(c => c.GetBlobClient("out/file.bin"))
            .Returns(blobMock.Object);

        using var stream = new MemoryStream(new byte[] { 0xAA });
        await _sut.WriteFileAsync("out/file.bin", stream);

        blobMock.Verify(b => b.UploadAsync(stream, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- DeleteFileAsync ---

    [Fact]
    public async Task DeleteFileAsync_DeletesBlob_WhenExists()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _containerMock.Setup(c => c.GetBlobClient("to-delete.txt"))
            .Returns(blobMock.Object);

        await _sut.DeleteFileAsync("to-delete.txt");

        blobMock.Verify(b => b.DeleteIfExistsAsync(
            It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteFileAsync_LogsDebug_WhenBlobNotFound()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        _containerMock.Setup(c => c.GetBlobClient("nope.txt"))
            .Returns(blobMock.Object);

        // Should not throw
        await _sut.DeleteFileAsync("nope.txt");
    }

    // --- DeleteDirectoryAsync ---

    [Fact]
    public async Task DeleteDirectoryAsync_DeletesAllBlobsUnderPrefix()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var page = Page<BlobItem>.FromValues(
            new[]
            {
                BlobsModelFactory.BlobItem("dir/file1.txt"),
                BlobsModelFactory.BlobItem("dir/file2.txt"),
            },
            continuationToken: null,
            Mock.Of<Response>());

        _containerMock.Setup(c => c.GetBlobsAsync(
                It.IsAny<BlobTraits>(), It.IsAny<BlobStates>(),
                "dir/", It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<BlobItem>.FromPages(new[] { page }));

        _containerMock.Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(blobMock.Object);

        await _sut.DeleteDirectoryAsync("dir");

        blobMock.Verify(b => b.DeleteIfExistsAsync(
            It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // --- NormalizePath (tested indirectly) ---

    [Fact]
    public async Task FileExistsAsync_NormalizesBackslashes()
    {
        var blobMock = new Mock<BlobClient>();
        blobMock.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _containerMock.Setup(c => c.GetBlobClient("some/path/file.txt"))
            .Returns(blobMock.Object);

        await _sut.FileExistsAsync(@"\some\path\file.txt");

        _containerMock.Verify(c => c.GetBlobClient("some/path/file.txt"), Times.Once);
    }
}
