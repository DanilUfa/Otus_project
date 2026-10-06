using System.Security.Cryptography;
using System.Text;

namespace VacationPlanner.Application.Services;

/// <summary>Генерация и хэширование refresh-токенов: клиенту — сырой токен, в БД — только SHA-256.</summary>
public static class RefreshTokenHash
{
    public static string Create()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
