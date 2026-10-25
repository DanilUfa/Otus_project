using VacationPlanner.Application.Contracts;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>No-op единица работы: тесты не трогают реальную транзакцию.</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task BeginAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
}
