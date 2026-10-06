using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<User?> GetByIdWithManagerAsync(int id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Сохраняет изменения (например, плавающий апгрейд хэша пароля).</summary>
    Task SaveAsync(CancellationToken ct = default);
}
