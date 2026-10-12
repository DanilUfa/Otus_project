using Microsoft.EntityFrameworkCore;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Infrastructure.Repositories;

public class OutboxRepository(AppDbContext db) : IOutboxRepository
{
    public Task AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        db.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task<List<OutboxMessage>> GetPendingAsync(int limit, CancellationToken ct = default)
        => db.OutboxMessages
            .Where(m => !m.PublishedAt.HasValue && !m.Failed)
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
