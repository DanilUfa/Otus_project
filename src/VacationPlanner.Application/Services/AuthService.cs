using Microsoft.Extensions.Configuration;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Аутентификация: логин (с плавающим апгрейдом legacy-хэша пароля), выдача пары
/// access+refresh, ротация refresh-токена, отзыв (logout). Refresh-токены хранятся
/// хэшированными и одноразовыми: каждое использование ротирует (старый отзывается).
/// </summary>
public class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokens,
    IConfiguration configuration)
{
    private const int DefaultRefreshDays = 14;

    private int RefreshDays =>
        int.TryParse(configuration["Jwt:RefreshExpiresDays"], out var d) && d > 0 ? d : DefaultRefreshDays;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct)
            ?? throw new UnauthorizedAccessException("Неверный email или пароль.");

        if (string.IsNullOrEmpty(request.Password) || !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Неверный email или пароль.");

        // Плавающий апгрейд: legacy SHA-256 (ранний черновик) -> PBKDF2 при успешном входе.
        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
            await userRepository.SaveAsync(ct);
        }

        return await IssuePairAsync(user, ct);
    }

    public async Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var token = await refreshTokens.GetByHashAsync(RefreshTokenHash.Hash(refreshToken), ct)
            ?? throw new UnauthorizedAccessException("Недействительный refresh-токен.");

        if (token.RevokedAt is not null || token.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh-токен отозван или истёк.");

        var user = await userRepository.GetByIdAsync(token.UserId, ct)
            ?? throw new UnauthorizedAccessException("Пользователь не найден.");

        return await RotateAsync(token, user, ct);
    }

    /// <summary>Выдаёт пару токенов только что зарегистрированному (подтверждённому) пользователю.</summary>
    public Task<LoginResponse> IssueInitialPairAsync(User user, CancellationToken ct = default)
        => IssuePairAsync(user, ct);

    /// <summary>Отзывает refresh-токен (logout). Идемпотентно.</summary>
    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var token = await refreshTokens.GetByHashAsync(RefreshTokenHash.Hash(refreshToken), ct);
        if (token is null || token.RevokedAt is not null) return;
        token.RevokedAt = DateTime.UtcNow;
        await refreshTokens.SaveAsync(ct);
    }

    private async Task<LoginResponse> IssuePairAsync(User user, CancellationToken ct)
    {
        var raw = RefreshTokenHash.Create();
        var token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = RefreshTokenHash.Hash(raw),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshDays)
        };
        await refreshTokens.AddAsync(token, ct);
        await refreshTokens.SaveAsync(ct);
        return new LoginResponse(tokenService.CreateAccessToken(user), raw, user.Id, user.Name, user.Role.ToString());
    }

    private async Task<LoginResponse> RotateAsync(RefreshToken old, User user, CancellationToken ct)
    {
        var raw = RefreshTokenHash.Create();
        var replacement = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = RefreshTokenHash.Hash(raw),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshDays)
        };
        old.RevokedAt = DateTime.UtcNow;
        old.ReplacedByTokenHash = replacement.TokenHash;

        await refreshTokens.AddAsync(replacement, ct);
        await refreshTokens.SaveAsync(ct);
        return new LoginResponse(tokenService.CreateAccessToken(user), raw, user.Id, user.Name, user.Role.ToString());
    }
}
