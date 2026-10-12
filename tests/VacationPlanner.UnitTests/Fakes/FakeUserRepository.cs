using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>In-memory IUserRepository: коллекция + счётчик сохранений (апгрейд хэша).</summary>
public sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();
    public int SaveCalls { get; private set; }

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByIdWithManagerAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<List<User>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(Users.ToList());

    public Task SaveAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }
}
