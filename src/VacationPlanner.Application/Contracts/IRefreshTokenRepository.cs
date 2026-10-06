using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
