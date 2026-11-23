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

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        if (user.Id == 0) user.Id = Users.Select(u => u.Id).DefaultIfEmpty().Max() + 1;
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }
}
