namespace VacationPlanner.Application.Contracts;

/// <summary>
/// Единица работы поверх общего AppDbContext: явная транзакция, в которую
/// можно включить НЕСКОЛЬКО SaveChanges (например: сохранить заявку → получить Id
/// → положить событие в outbox с правильным Id → закоммитить всё атомарно).
/// </summary>
public interface IUnitOfWork
{
    Task BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
