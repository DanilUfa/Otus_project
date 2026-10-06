using VacationPlanner.Application.Services;

namespace VacationPlanner.UnitTests;

/// <summary>
/// BalanceGate: per-user async-замок (SemaphoreSlim).
/// Один пользователь — сериализация пересчёта; разные пользователи не блокируют друг друга.
/// </summary>
public class BalanceGateTests
{
    [Fact]
    public async Task Same_user_locks_are_serialized()
    {
        var gate = new BalanceGate();
        var first = await gate.LockAsync(1);
        var second = gate.LockAsync(1);
        await Task.Delay(120);
        Assert.False(second.IsCompleted);
        first.Dispose();
        using var _ = await second;
    }

    [Fact]
    public async Task Different_users_do_not_block_each_other()
    {
        var gate = new BalanceGate();
        using var held = gate.Lock(1);
        var other = await gate.LockAsync(2);
        using var _ = other;
    }

    [Fact]
    public async Task Lock_can_be_reacquired_after_release()
    {
        var gate = new BalanceGate();
        (await gate.LockAsync(5)).Dispose();
        using var second = await gate.LockAsync(5);
    }

    [Fact]
    public async Task LockAsync_honors_cancellation()
    {
        var gate = new BalanceGate();
        using var held = await gate.LockAsync(7);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => gate.LockAsync(7, cts.Token));
    }
}
