using Microsoft.EntityFrameworkCore;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Infrastructure.Repositories;

public class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public Task<int> DeleteExpiredAndRevokedAsync(DateTime revokedBefore, CancellationToken ct = default)
        => db.RefreshTokens
            .Where(t => t.ExpiresAt < DateTime.UtcNow || (t.RevokedAt != null && t.RevokedAt < revokedBefore))
            .ExecuteDeleteAsync(ct);
}
