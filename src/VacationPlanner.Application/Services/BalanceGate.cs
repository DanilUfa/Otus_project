using System.Collections.Concurrent;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Сериализует расчёт баланса и изменения заявок одного сотрудника через SemaphoreSlim.
/// Зарегистрирован как Singleton; синхронизация действует в пределах одного процесса API.
/// </summary>
public class BalanceGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    /// <summary>Захватывает блокировку сотрудника; Dispose освобождает её.</summary>
    public IDisposable Lock(int userId)
    {
        var sem = _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        return new Releaser(sem);
    }

    /// <summary>Async-версия (без блокировки потока).</summary>
    public async Task<IDisposable> LockAsync(int userId, CancellationToken ct = default)
    {
        var sem = _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        public void Dispose() => sem.Release();
    }
}
