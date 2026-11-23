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

    public async Task<bool> TryRotateAsync(RefreshToken old, RefreshToken replacement, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        // PostgreSQL rechecks this predicate after waiting for a concurrent updater's row lock.
        var changed = await db.RefreshTokens
            .Where(t => t.TokenHash == old.TokenHash && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenHash, replacement.TokenHash), ct);
        if (changed != 1) return false; // disposing rolls the transaction back
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task<int> DeleteExpiredAndRevokedAsync(DateTime revokedBefore, CancellationToken ct = default)
        => db.RefreshTokens
            .Where(t => t.ExpiresAt < DateTime.UtcNow || (t.RevokedAt != null && t.RevokedAt < revokedBefore))
            .ExecuteDeleteAsync(ct);
}
