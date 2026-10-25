using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>In-memory transaction outbox (события, ожидающие публикации).</summary>
public sealed class FakeOutboxRepository : IOutboxRepository
{
    public List<OutboxMessage> Items { get; } = new();

    public Task AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        Items.Add(message);
        return Task.CompletedTask;
    }

    public Task<List<OutboxMessage>> GetPendingAsync(int limit, CancellationToken ct = default)
        => Task.FromResult(Items.Where(m => !m.PublishedAt.HasValue && !m.Failed).Take(limit).ToList());

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
}
