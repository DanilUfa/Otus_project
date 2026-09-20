using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Patterns;
using VacationPlanner.Domain;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Фоновая политика: закрывает «зависшие» черновики (Draft → Cancelled).
/// Переход выполняется только через State-машину (легальный ли он).
/// </summary>
public class StaleDraftService(ILeaveRepository repo)
{
    public async Task<int> CancelStaleDraftsAsync(DateTime olderThan, CancellationToken ct = default)
    {
        var stale = await repo.GetStaleDraftRequestsAsync(olderThan, ct);
        foreach (var lr in stale)
        {
            LeaveState.For(lr.Status).EnsureCanTransitionTo(LeaveStatus.Cancelled); // Draft → Cancelled
            lr.Status = LeaveStatus.Cancelled;
        }
        if (stale.Count > 0)
            await repo.SaveAsync(ct);
        return stale.Count;
    }
}