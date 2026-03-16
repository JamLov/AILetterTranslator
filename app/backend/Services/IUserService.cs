namespace LetterTranslation.Api.Services;

public interface IUserService
{
    bool IsUserAllowed(string? email);
}
