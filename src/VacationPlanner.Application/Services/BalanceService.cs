using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Расчёт остатка дней годового отпуска.
/// Логика выделена в отдельный сервис — её легко расширить стратегиями по типам отпуска.
/// </summary>
public class BalanceService(ILeaveRepository leaveRepository, BalanceGate gate, IDistributedCache? cache = null)
{
    public const int DefaultAnnualDays = 28;

    public async Task<int> GetRemainingAnnualDaysAsync(int userId, int year, CancellationToken ct = default)
    {
        // The same per-user lock surrounds cache reads/fills and leave changes + invalidation.
        // An in-flight reader cannot refill the old balance after a successful invalidation.
        using var _ = await gate.LockAsync(userId, ct);

        var key = CacheKey(userId, year);
        if (cache is not null && await cache.GetAsync(key, ct) is { } bytes)
            return JsonSerializer.Deserialize<int>(bytes);

        var from = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var requests = await leaveRepository.GetByUserInRangeAsync(userId, from, to, ct);

        // Дни учитываются только в пределах запрашиваемого года: отпуск через границу
        // (25.12–05.01) не задваивается — в каждый год попадает своя часть периода.
        // «На рассмотрении» тоже резервирует дни: нельзя набрать заявок больше лимита
        // и «для себя» пересечься с сотрудником, чей отпуск ещё согласовывается.
        var used = requests
            .Where(r => r.Type == LeaveType.Annual &&
                        r.Status is LeaveStatus.Approved or LeaveStatus.Submitted)
            .Sum(r => DaysWithinYear(r.StartDate.Date, r.EndDate.Date, year));

        var remaining = Math.Max(0, DefaultAnnualDays - used);
        if (cache is not null)
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(remaining),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) }, ct);
        return remaining;
    }

    // LeaveService holds BalanceGate for this employee and calls this only after commit.
    // Do not reacquire the non-reentrant lock here. Complete invalidation even if the caller disconnects.
    internal async Task InvalidateAsync(LeaveRequest leave)
    {
        if (cache is null || leave.Type != LeaveType.Annual) return;
        for (var year = leave.StartDate.Year; year <= leave.EndDate.Year; year++)
            await cache.RemoveAsync(CacheKey(leave.UserId, year), CancellationToken.None);
    }

    private static string CacheKey(int userId, int year) => $"balance:v2:{userId}:{year}";

    /// <summary>Число дней отпуска, приходящееся на указанный год (пересечение периода с годом).</summary>
    internal static int DaysWithinYear(DateTime start, DateTime end, int year)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);
        var s = start < yearStart ? yearStart : start;
        var e = end > yearEnd ? yearEnd : end;
        return s <= e ? (e - s).Days + 1 : 0;
    }
}
