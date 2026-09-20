using Microsoft.AspNetCore.SignalR;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Web.Hubs;

namespace VacationPlanner.Web.Realtime;

/// <summary>
/// АДАПТЕР: реализует контракт <see cref="IRealtimeNotifier"/> поверх SignalR.
/// Application не знает про SignalR — только про интерфейс.
/// </summary>
public class RealtimeNotifier(IHubContext<VacationHub> hub) : IRealtimeNotifier
{
    public Task NotifyLeaveCreatedAsync(LeaveRequestDto dto, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("LeaveCreated", dto, ct);

    public Task NotifyStatusChangedAsync(int requestId, string status, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("LeaveStatusChanged", new { requestId, status }, ct);
}