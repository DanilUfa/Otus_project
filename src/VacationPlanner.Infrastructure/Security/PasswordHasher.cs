using System.Security.Cryptography;
using System.Text;
using VacationPlanner.Application.Contracts;

namespace VacationPlanner.Infrastructure.Security;

/// <summary>
/// Хэширование паролей: PBKDF2 (Rfc2898, SHA-256, 100k итераций, per-user соль).
/// Совместимость с legacy-форматом раннего черновика (SHA-256 без соли): Verify пробует
/// PBKDF2, затем legacy; NeedsRehash даёт AuthService перехешировать пароль при входе.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Prefix = "PBKDF2:";

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;
        return hash.StartsWith(Prefix, StringComparison.Ordinal)
            ? VerifyPbkdf2(password, hash)
            : VerifyLegacySha256(password, hash);
    }

    public bool NeedsRehash(string hash)
        => string.IsNullOrEmpty(hash) || !hash.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool VerifyPbkdf2(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < 10_000)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private static bool VerifyLegacySha256(string password, string hash)
    {
        var candidate = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        return candidate == hash;
    }
}
