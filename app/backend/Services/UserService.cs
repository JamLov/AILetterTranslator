namespace LetterTranslation.Api.Services;

public class UserService : IUserService
{
    private readonly IConfiguration _config;
    private readonly ILogger<UserService> _logger;

    public UserService(IConfiguration config, ILogger<UserService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsUserAllowed(string? email)
    {
        var allowedUsers = _config.GetSection("AllowedUsers").Get<string[]>() ?? Array.Empty<string>();
        
        if (string.IsNullOrEmpty(email) || !allowedUsers.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Access/Login denied. Email '{Email}' is not in the allowed users list.", email ?? "null");
            return false;
        }

        return true;
    }
}
