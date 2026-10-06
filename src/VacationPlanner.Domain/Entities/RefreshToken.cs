namespace VacationPlanner.Domain.Entities;

/// <summary>Refresh-токен (ротация/отзыв). В БД хранится только SHA-256 хэш токена.</summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Момент отзыва (logout / ротация). null — токен активен.</summary>
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
