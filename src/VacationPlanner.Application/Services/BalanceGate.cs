using System.Collections.Concurrent;

namespace VacationPlanner.Application.Services;

/// <summary>
/// PATTERN: Примитив синхронизации (SemaphoreSlim).
///
/// Баланс дней — общий ресурс: несколько Worker'ов могут одновременно добавлять
/// заявки по одному пользователю, и была бы гонка (double-spend дней).
/// Здесь каждому пользователю даётся свой async-замок, чтобы пересчёт был атомарным
/// по одному сотруднику. Экземпляр синглтон.
/// </summary>
public class BalanceGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    /// <summary>Берёт async-замок по пользователю. Верни в using.</summary>
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