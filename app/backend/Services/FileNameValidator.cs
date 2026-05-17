namespace LetterTranslation.Api.Services;

public static class FileNameValidator
{
    private static readonly HashSet<char> InvalidChars = new(Path.GetInvalidFileNameChars());

    public static bool IsSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (fileName is "." or "..")
            return false;
        if (fileName.Any(c => InvalidChars.Contains(c)))
            return false;
        if (Path.GetFileName(fileName) != fileName)
            return false;
        return true;
    }

    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png" };

    public static string? GetImageContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => null
        };
    }
}
