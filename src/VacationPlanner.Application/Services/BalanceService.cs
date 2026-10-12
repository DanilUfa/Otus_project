using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Расчёт остатка дней годового отпуска.
/// Логика выделена в отдельный сервис — её легко расширить стратегиями по типам отпуска.
/// </summary>
public class BalanceService(ILeaveRepository leaveRepository, BalanceGate gate)
{
    public const int DefaultAnnualDays = 28;

    public async Task<int> GetRemainingAnnualDaysAsync(int userId, int year, CancellationToken ct = default)
    {
        // Примитив синхронизации: пересчёт остатка — общий ресурс по пользователю.
        // Несколько одновременных запросов по одному сотруднику сериализуются
        // per-user async-замком (SemaphoreSlim), защищая от двойного списания дней.
        using var _ = await gate.LockAsync(userId, ct);

        var from = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var requests = await leaveRepository.GetByUserInRangeAsync(userId, from, to, ct);

        // Дни учитываются ТОЛЬКО в пределах запрашиваемого года: отпуск через границу
        // (25.12–05.01) не задваивается — в каждый год попадает своя часть периода.
        var used = requests
            .Where(r => r.Type == LeaveType.Annual && r.Status == LeaveStatus.Approved)
            .Sum(r => DaysWithinYear(r.StartDate.Date, r.EndDate.Date, year));

        return Math.Max(0, DefaultAnnualDays - used);
    }

    /// <summary>Число дней отпуска, приходящееся на указанный год (пересечение периода с годом).</summary>
    private static int DaysWithinYear(DateTime start, DateTime end, int year)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);
        var s = start < yearStart ? yearStart : start;
        var e = end > yearEnd ? yearEnd : end;
        return s <= e ? (e - s).Days + 1 : 0;
    }
}