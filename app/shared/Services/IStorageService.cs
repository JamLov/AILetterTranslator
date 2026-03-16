namespace LetterTranslation.Shared.Services;

public interface IStorageService
{
    Task EnsureDirectoryAsync(string path);
    Task WriteTextAsync(string path, string content);
    Task WriteFileAsync(string path, Stream content);
    Task<bool> DirectoryExistsAsync(string path);
    Task<IEnumerable<string>> GetDirectoriesAsync(string path);
    Task<IEnumerable<string>> GetFileNamesAsync(string path);
    Task<bool> FileExistsAsync(string path);
    Task DeleteFileAsync(string path);
    Task<string> ReadTextAsync(string path);
    Task<byte[]> ReadBytesAsync(string path);
    Task MoveDirectoryAsync(string sourcePath, string destinationPath);
    Task DeleteDirectoryAsync(string path);
}
