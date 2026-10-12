using Xunit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.UnitTests.Fakes;

namespace VacationPlanner.UnitTests;

/// <summary>
/// Аутентификация: ошибки (401), выдача пары, плавающий апгрейд legacy-хэша,
/// ротация refresh-токена (старый отзывается, новый одноразовый), идемпотентный logout.
/// В БД хранится только SHA-256 хэш refresh-токена — клиент получает только сырой.
/// </summary>
public class AuthServiceTests
{
    private static DateTime UtcNow() => DateTime.UtcNow;
    private static void AssertNear(DateTime actual, DateTime expected, double seconds = 5)
        => Assert.InRange(actual, expected.AddSeconds(-seconds), expected.AddSeconds(seconds));

    private static User User(int id = 7, string email = "petr@vac.local", UserRole role = UserRole.Employee)
        => new()
        {
            Id = id,
            Name = "Пётр",
            Email = email,
            Role = role,
            PasswordHash = "PBKDF2:fake:secret",
        };

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeRefreshTokenRepository Tokens { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeTokenService TokensSvc { get; } = new();
        public AuthService Auth { get; }

        public Harness(params (string Key, string Value)[] config)
        {
            Auth = new AuthService(
                Users, Hasher, TokensSvc, Tokens, new FakeConfiguration(config));
        }
    }

    [Fact]
    public async Task LoginAsync_unknown_email_throws_unauthorized()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.LoginAsync(new LoginRequest("nobody@vac.local", "secret")));
        Assert.Equal(0, h.Users.SaveCalls);
        Assert.Empty(h.Tokens.Items);
    }

    [Fact]
    public async Task LoginAsync_wrong_password_throws_unauthorized()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.LoginAsync(new LoginRequest("petr@vac.local", "wrong")));
        Assert.Equal(0, h.Users.SaveCalls);
        Assert.Empty(h.Tokens.Items);
    }

    [Fact]
    public async Task LoginAsync_empty_password_throws_unauthorized()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.LoginAsync(new LoginRequest("petr@vac.local", "")));
    }

    [Fact]
    public async Task LoginAsync_success_returns_pair_and_persists_only_hash()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        var before = UtcNow();

        var resp = await h.Auth.LoginAsync(new LoginRequest("petr@vac.local", "secret"));

        Assert.Equal("at:7", resp.Token);
        Assert.Equal(7, resp.UserId);
        Assert.Equal("Пётр", resp.Name);
        Assert.Equal("Employee", resp.Role);
        // клиенту — сырой токен; в хранилище — только его SHA-256 хэш
        var stored = Assert.Single(h.Tokens.Items);
        Assert.Equal(RefreshTokenHash.Hash(resp.RefreshToken), stored.TokenHash);
        Assert.NotEqual(resp.RefreshToken, stored.TokenHash);
        AssertNear(stored.ExpiresAt, before.AddDays(14));
        Assert.Null(stored.RevokedAt);
        Assert.Null(stored.ReplacedByTokenHash);
        Assert.Equal(1, h.Tokens.SaveCalls);
        Assert.Equal(0, h.Users.SaveCalls); // хэш PBKDF2 — апгрейд не нужен
    }

    [Fact]
    public async Task LoginAsync_legacy_hash_is_upgraded_on_success()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        h.Hasher.NeedsRehashResult = true; // legacy SHA-256 из раннего черновика

        await h.Auth.LoginAsync(new LoginRequest("petr@vac.local", "secret"));

        var user = h.Users.Users.Single();
        Assert.Equal("PBKDF2:fake:secret", user.PasswordHash);
        Assert.Equal(1, h.Users.SaveCalls);
        Assert.Equal(1, h.Hasher.HashCalls);
    }

    [Fact]
    public async Task LoginAsync_refresh_lifetime_follows_configuration()
    {
        var h = new Harness(("Jwt:RefreshExpiresDays", "30"));
        h.Users.Users.Add(User());
        var before = UtcNow();

        await h.Auth.LoginAsync(new LoginRequest("petr@vac.local", "secret"));

        AssertNear(h.Tokens.Items.Single().ExpiresAt, before.AddDays(30));
    }

    [Fact]
    public async Task RefreshAsync_unknown_token_throws()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.RefreshAsync("no-such-token"));
    }

    [Fact]
    public async Task RefreshAsync_revoked_token_throws()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        h.Tokens.Items.Add(new RefreshToken
        {
            UserId = 7,
            TokenHash = RefreshTokenHash.Hash("old-raw"),
            CreatedAt = UtcNow().AddDays(-3),
            ExpiresAt = UtcNow().AddDays(11),
            RevokedAt = UtcNow().AddDays(-1),
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.RefreshAsync("old-raw"));
    }

    [Fact]
    public async Task RefreshAsync_expired_token_throws()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        h.Tokens.Items.Add(new RefreshToken
        {
            UserId = 7,
            TokenHash = RefreshTokenHash.Hash("expired-raw"),
            CreatedAt = UtcNow().AddDays(-20),
            ExpiresAt = UtcNow().AddDays(-6), // уже истёк
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.RefreshAsync("expired-raw"));
    }

    [Fact]
    public async Task RefreshAsync_missing_user_throws()
    {
        var h = new Harness();
        h.Tokens.Items.Add(new RefreshToken
        {
            UserId = 999, // пользователя нет
            TokenHash = RefreshTokenHash.Hash("orphan-raw"),
            CreatedAt = UtcNow().AddDays(-1),
            ExpiresAt = UtcNow().AddDays(13),
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => h.Auth.RefreshAsync("orphan-raw"));
    }

    [Fact]
    public async Task RefreshAsync_rotates_token_old_revoked_and_replaced()
    {
        var h = new Harness();
        h.Users.Users.Add(User());
        h.Tokens.Items.Add(new RefreshToken
        {
            Id = 1,
            UserId = 7,
            TokenHash = RefreshTokenHash.Hash("old-raw"),
            CreatedAt = UtcNow().AddDays(-5),
            ExpiresAt = UtcNow().AddDays(9),
        });

        var resp = await h.Auth.RefreshAsync("old-raw");

        var old = h.Tokens.Items.Single(t => t.TokenHash == RefreshTokenHash.Hash("old-raw"));
        Assert.NotNull(old.RevokedAt); // одноразовость: старый отозван
        var newToken = h.Tokens.Items.Single(t => t.Id != 1);
        Assert.Equal(old.ReplacedByTokenHash, newToken.TokenHash);
        Assert.Equal(RefreshTokenHash.Hash(resp.RefreshToken), newToken.TokenHash);
        Assert.Null(newToken.RevokedAt);
        AssertNear(newToken.ExpiresAt, UtcNow().AddDays(14));
        Assert.Equal("at:7", resp.Token);
    }

    [Fact]
    public async Task LogoutAsync_revokes_token()
    {
        var h = new Harness();
        h.Tokens.Items.Add(new RefreshToken
        {
            UserId = 7,
            TokenHash = RefreshTokenHash.Hash("live-raw"),
            CreatedAt = UtcNow().AddDays(-1),
            ExpiresAt = UtcNow().AddDays(13),
        });
        await h.Auth.LogoutAsync("live-raw");
        Assert.NotNull(h.Tokens.Items.Single().RevokedAt);
        Assert.Equal(1, h.Tokens.SaveCalls);
    }

    [Fact]
    public async Task LogoutAsync_unknown_token_is_silent_noop()
    {
        var h = new Harness();
        await h.Auth.LogoutAsync("never-existed");
        Assert.Empty(h.Tokens.Items);
        Assert.Equal(0, h.Tokens.SaveCalls);
    }

    [Fact]
    public async Task LogoutAsync_already_revoked_is_silent_noop()
    {
        var h = new Harness();
        h.Tokens.Items.Add(new RefreshToken
        {
            UserId = 7,
            TokenHash = RefreshTokenHash.Hash("dead-raw"),
            CreatedAt = UtcNow().AddDays(-4),
            ExpiresAt = UtcNow().AddDays(10),
            RevokedAt = UtcNow().AddDays(-2),
        });
        await h.Auth.LogoutAsync("dead-raw");
        Assert.Equal(0, h.Tokens.SaveCalls); // идемпотентно: повторный отзыв ничего не делает
    }
}
