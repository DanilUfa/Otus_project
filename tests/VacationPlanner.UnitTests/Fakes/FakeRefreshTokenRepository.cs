using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>
/// In-memory IRefreshTokenRepository: поиск/добавление/сохранение; Delete* запоминает
/// оборотный аргумент и возвращает заданное число (для проверки политки чистки).
/// </summary>
public sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    public List<RefreshToken> Items { get; } = new();
    public int SaveCalls { get; private set; }
    public DateTime? LastRevokedBefore { get; private set; }
    public int DeletedCountToReturn { get; set; }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        Items.Add(token);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredAndRevokedAsync(DateTime revokedBefore, CancellationToken ct = default)
    {
        LastRevokedBefore = revokedBefore;
        return Task.FromResult(DeletedCountToReturn);
    }
}
