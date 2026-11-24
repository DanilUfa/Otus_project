using System.Collections.Concurrent;

namespace VacationPlanner.Web.Auth;

/// <summary>
/// Ограничивает попытки входа по email в пределах 15-минутного окна.
/// Счётчики хранятся в памяти одного экземпляра API; для нескольких экземпляров
/// требуется общее хранилище счётчиков.
/// </summary>
public sealed class LoginThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 5;

    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _attempts = new();
    private DateTime _lastCleanup = DateTime.UtcNow;

    public bool TryEnter(string email)
    {
        CleanupExpired();
        if (!_attempts.TryGetValue(email, out var entry)) return true;
        return DateTime.UtcNow - entry.WindowStart > Window || entry.Count < MaxAttempts;
    }

    public void RegisterFailure(string email)
    {
        CleanupExpired();
        _attempts.AddOrUpdate(email,
            _ => (1, DateTime.UtcNow),
            (_, cur) => DateTime.UtcNow - cur.WindowStart > Window
                ? (1, DateTime.UtcNow)
                : (cur.Count + 1, cur.WindowStart));
    }

    public void Reset(string email) => _attempts.TryRemove(email, out _);

    /// <summary>
    /// Ленивая очистка: раз в минуту выкидываем истёкшие окна, чтобы словарь
    /// не рос бесконечно от перебора email'ов (память одного инстанса).
    /// </summary>
    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < TimeSpan.FromMinutes(1)) return;
        _lastCleanup = now;
        foreach (var (email, entry) in _attempts)
            if (now - entry.WindowStart > Window) _attempts.TryRemove(email, out _);
    }
}
