using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// Удаляет непригодные токены: истёкшие (ExpiresAt &lt; now) и отозванные ранее
    /// <paramref name="revokedBefore"/> (grace-период после отзыва/ротации). Возвращает число удалённых.
    /// </summary>
    Task<int> DeleteExpiredAndRevokedAsync(DateTime revokedBefore, CancellationToken ct = default);
}
