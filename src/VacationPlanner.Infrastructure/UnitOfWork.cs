using Microsoft.EntityFrameworkCore.Storage;
using VacationPlanner.Application.Contracts;

namespace VacationPlanner.Infrastructure;

public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    private IDbContextTransaction? _tx;

    public Task BeginAsync(CancellationToken ct = default)
    {
        _tx ??= db.Database.CurrentTransaction;
        return _tx is null ? BeginCoreAsync(ct) : Task.CompletedTask;
    }

    private async Task BeginCoreAsync(CancellationToken ct)
        => _tx = await db.Database.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_tx is not null)
        {
            await _tx.CommitAsync(ct);
            await _tx.DisposeAsync();
            _tx = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_tx is not null)
        {
            await _tx.RollbackAsync(ct);
            await _tx.DisposeAsync();
            _tx = null;
        }
    }
}
