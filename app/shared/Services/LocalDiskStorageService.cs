using Microsoft.Extensions.Logging;

namespace LetterTranslation.Shared.Services;

public class LocalDiskStorageService : IStorageService
{
    private readonly ILogger<LocalDiskStorageService> _logger;

    public LocalDiskStorageService(ILogger<LocalDiskStorageService> logger)
    {
        _logger = logger;
    }

    public Task EnsureDirectoryAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            _logger.LogInformation("Created directory at {Path}", path);
        }
        else
        {
            _logger.LogDebug("Directory already exists at {Path}", path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> DirectoryExistsAsync(string path)
    {
        return Task.FromResult(Directory.Exists(path));
    }

    public Task<IEnumerable<string>> GetDirectoriesAsync(string path)
    {
        return Task.FromResult(Directory.GetDirectories(path).AsEnumerable());
    }

    public Task<IEnumerable<string>> GetFileNamesAsync(string path)
    {
        var filePaths = Directory.GetFiles(path);
        var fileNames = filePaths.Select(Path.GetFileName).Where(f => f != null).Select(f => f!);
        return Task.FromResult(fileNames);
    }

    public Task<bool> FileExistsAsync(string path)
    {
        return Task.FromResult(File.Exists(path));
    }

    public Task DeleteFileAsync(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted file at {Path}", path);
        }
        else
        {
            _logger.LogDebug("File not found for deletion at {Path}", path);
        }

        return Task.CompletedTask;
    }

    public async Task WriteTextAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            await EnsureDirectoryAsync(directory);
        }

        await File.WriteAllTextAsync(path, content);
        _logger.LogInformation("Wrote text to {Path}", path);
    }

    public async Task<string> ReadTextAsync(string path)
    {
        return await File.ReadAllTextAsync(path);
    }

    public async Task<byte[]> ReadBytesAsync(string path)
    {
        return await File.ReadAllBytesAsync(path);
    }

    public Task MoveDirectoryAsync(string sourcePath, string destinationPath)
    {
        var destinationParent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationParent) && !Directory.Exists(destinationParent))
        {
            Directory.CreateDirectory(destinationParent);
        }

        Directory.Move(sourcePath, destinationPath);
        _logger.LogInformation("Moved directory from {Source} to {Destination}", sourcePath, destinationPath);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            _logger.LogInformation("Deleted directory at {Path}", path);
        }
        else
        {
            _logger.LogDebug("Directory not found for deletion at {Path}", path);
        }

        return Task.CompletedTask;
    }

    public async Task WriteFileAsync(string path, Stream content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            await EnsureDirectoryAsync(directory);
        }

        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await content.CopyToAsync(fileStream);
        _logger.LogInformation("Wrote file stream to {Path}", path);
    }

    public async Task CopyFileAsync(string sourcePath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            await EnsureDirectoryAsync(directory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        _logger.LogInformation("Copied file from {Source} to {Destination}", sourcePath, destinationPath);
    }
}
