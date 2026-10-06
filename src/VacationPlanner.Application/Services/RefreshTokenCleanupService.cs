using Microsoft.Extensions.Configuration;
using VacationPlanner.Application.Contracts;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Фоновая политика: чистка недействительных refresh-токенов.
/// Истёкшие удаляются сразу; отозванные — после grace-периода
/// (RefreshTokenCleanup:CleanupGraceDays, по умолчанию 1 день), чтобы ротации
/// успели попасть в логи/аудит. Выполняется отдельным процессом (scheduler).
/// </summary>
public class RefreshTokenCleanupService(IRefreshTokenRepository refreshTokens, IConfiguration configuration)
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromDays(1);

    private TimeSpan Grace =>
        TimeSpan.FromDays(Math.Max(0, int.TryParse(configuration["RefreshTokenCleanup:CleanupGraceDays"], out var d) ? d : 1));

    /// <summary>Возвращает число удалённых записей (для логов).</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
        => await refreshTokens.DeleteExpiredAndRevokedAsync(DateTime.UtcNow - Grace, ct);
}
