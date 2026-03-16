using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LetterTranslation.Shared.Services;

public class AzureBlobStorageService : IStorageService
{
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly BlobContainerClient _containerClient;

    public AzureBlobStorageService(ILogger<AzureBlobStorageService> logger, IConfiguration config)
    {
        _logger = logger;

        var connectionString = config["AzureBlob:ConnectionString"]
            ?? throw new InvalidOperationException("AzureBlob:ConnectionString is not configured.");
        var containerName = config["AzureBlob:ContainerName"] ?? "letter-translation";

        _containerClient = new BlobContainerClient(connectionString, containerName);
        _containerClient.CreateIfNotExists();

        _logger.LogInformation("Azure Blob Storage initialized for container '{Container}'", containerName);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    public Task EnsureDirectoryAsync(string path)
    {
        // No-op: blob storage has no real directories
        _logger.LogDebug("EnsureDirectoryAsync is a no-op for blob storage: {Path}", path);
        return Task.CompletedTask;
    }

    public async Task<bool> DirectoryExistsAsync(string path)
    {
        var prefix = NormalizePath(path).TrimEnd('/') + "/";

        await foreach (var _ in _containerClient.GetBlobsAsync(prefix: prefix).AsPages(pageSizeHint: 1))
        {
            return true;
        }

        return false;
    }

    public async Task<IEnumerable<string>> GetDirectoriesAsync(string path)
    {
        var prefix = NormalizePath(path).TrimEnd('/') + "/";
        var directories = new List<string>();

        await foreach (var item in _containerClient.GetBlobsByHierarchyAsync(delimiter: "/", prefix: prefix))
        {
            if (item.IsPrefix)
            {
                // Return without trailing slash to match Directory.GetDirectories behavior
                directories.Add(item.Prefix.TrimEnd('/'));
            }
        }

        _logger.LogDebug("Found {Count} directories under '{Prefix}'", directories.Count, prefix);
        return directories;
    }

    public async Task<IEnumerable<string>> GetFileNamesAsync(string path)
    {
        var prefix = NormalizePath(path).TrimEnd('/') + "/";
        var fileNames = new List<string>();

        await foreach (var item in _containerClient.GetBlobsByHierarchyAsync(delimiter: "/", prefix: prefix))
        {
            if (item.IsBlob)
            {
                // Return just the file name, matching LocalDiskStorageService behavior
                var name = item.Blob.Name;
                var lastSlash = name.LastIndexOf('/');
                fileNames.Add(lastSlash >= 0 ? name[(lastSlash + 1)..] : name);
            }
        }

        _logger.LogDebug("Found {Count} files under '{Prefix}'", fileNames.Count, prefix);
        return fileNames;
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        var response = await blobClient.ExistsAsync();
        return response.Value;
    }

    public async Task<string> ReadTextAsync(string path)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        var response = await blobClient.DownloadContentAsync();
        return response.Value.Content.ToString();
    }

    public async Task<byte[]> ReadBytesAsync(string path)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        var response = await blobClient.DownloadContentAsync();
        return response.Value.Content.ToArray();
    }

    public async Task WriteTextAsync(string path, string content)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true);
        _logger.LogInformation("Wrote text to blob '{Path}'", path);
    }

    public async Task WriteFileAsync(string path, Stream content)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        await blobClient.UploadAsync(content, overwrite: true);
        _logger.LogInformation("Wrote file stream to blob '{Path}'", path);
    }

    public async Task DeleteFileAsync(string path)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        var deleted = await blobClient.DeleteIfExistsAsync();

        if (deleted.Value)
            _logger.LogInformation("Deleted blob '{Path}'", path);
        else
            _logger.LogDebug("Blob not found for deletion: '{Path}'", path);
    }
}
