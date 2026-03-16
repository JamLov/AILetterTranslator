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
        using var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream);
        if (stream.Length == 0) return string.Empty;
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public async Task<byte[]> ReadBytesAsync(string path)
    {
        var blobClient = _containerClient.GetBlobClient(NormalizePath(path));
        using var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream);
        return stream.ToArray();
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

    public async Task MoveDirectoryAsync(string sourcePath, string destinationPath)
    {
        var sourcePrefix = NormalizePath(sourcePath).TrimEnd('/') + "/";
        var destPrefix = NormalizePath(destinationPath).TrimEnd('/') + "/";

        var blobsToDelete = new List<BlobClient>();

        await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: sourcePrefix))
        {
            var relativePath = blobItem.Name[sourcePrefix.Length..];
            var destBlobPath = destPrefix + relativePath;

            var sourceBlob = _containerClient.GetBlobClient(blobItem.Name);
            var destBlob = _containerClient.GetBlobClient(destBlobPath);

            await destBlob.StartCopyFromUriAsync(sourceBlob.Uri);

            // Wait for copy to complete
            BlobProperties properties;
            do
            {
                properties = (await destBlob.GetPropertiesAsync()).Value;
                if (properties.CopyStatus == CopyStatus.Failed)
                    throw new InvalidOperationException($"Copy failed for blob '{blobItem.Name}': {properties.CopyStatusDescription}");
            } while (properties.CopyStatus == CopyStatus.Pending);

            blobsToDelete.Add(sourceBlob);
        }

        foreach (var blob in blobsToDelete)
        {
            await blob.DeleteIfExistsAsync();
        }

        _logger.LogInformation("Moved directory from '{Source}' to '{Destination}' ({Count} blobs)", sourcePath, destinationPath, blobsToDelete.Count);
    }

    public async Task DeleteDirectoryAsync(string path)
    {
        var prefix = NormalizePath(path).TrimEnd('/') + "/";
        var count = 0;

        await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix))
        {
            var blobClient = _containerClient.GetBlobClient(blobItem.Name);
            await blobClient.DeleteIfExistsAsync();
            count++;
        }

        _logger.LogInformation("Deleted directory '{Path}' ({Count} blobs)", path, count);
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
