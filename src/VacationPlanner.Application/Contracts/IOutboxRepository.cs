using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

/// <summary>Репозиторий transaction outbox (сообщения для публикации в шину).</summary>
public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken ct = default);
    Task<List<OutboxMessage>> GetPendingAsync(int limit, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
