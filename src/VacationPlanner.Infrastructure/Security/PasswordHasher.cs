using System.Security.Cryptography;
using System.Text;
using VacationPlanner.Application.Contracts;

namespace VacationPlanner.Infrastructure.Security;

/// <summary>
/// Хэширование паролей (SHA-256 + соль по email-префиксу). Черновик:
/// в прод-варианте заменим на ASP.NET Identity (PBKDF2) или Keycloak.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;
        return Hash(password) == hash;
    }
}