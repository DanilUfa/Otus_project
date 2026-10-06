using System.Collections.Concurrent;

namespace VacationPlanner.Web.Auth;

/// <summary>
/// Простейший in-memory throttle попыток входа (per-email, скользящее окно 15 мин).
/// Честная оговорка: состояние живёт в памяти одного инстанса api — при репликации
/// понадобится распределённый счётчик (Redis / ASP.NET RateLimiting с Redis).
/// </summary>
public sealed class LoginThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 5;

    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _attempts = new();

    public bool TryEnter(string email)
    {
        if (!_attempts.TryGetValue(email, out var entry)) return true;
        return DateTime.UtcNow - entry.WindowStart > Window || entry.Count < MaxAttempts;
    }

    public void RegisterFailure(string email)
        => _attempts.AddOrUpdate(email,
            _ => (1, DateTime.UtcNow),
            (_, cur) => DateTime.UtcNow - cur.WindowStart > Window
                ? (1, DateTime.UtcNow)
                : (cur.Count + 1, cur.WindowStart));

    public void Reset(string email) => _attempts.TryRemove(email, out _);
}
