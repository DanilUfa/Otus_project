using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface ITokenService
{
    /// <summary>Создаёт JWT access-токен (короткоживущий, содержит claims пользователя).</summary>
    string CreateAccessToken(User user);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);

    /// <summary>true, если хэш в legacy-формате и его стоит перехешировать при успешном входе.</summary>
    bool NeedsRehash(string hash);
}
