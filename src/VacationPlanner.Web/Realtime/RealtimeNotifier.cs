using Microsoft.AspNetCore.SignalR;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Web.Hubs;

namespace VacationPlanner.Web.Realtime;

/// <summary>
/// АДАПТЕР: реализует контракт <see cref="IRealtimeNotifier"/> поверх SignalR.
/// Доставка точечная: группа "u:{userId}" — владелец заявки плюс (при создании)
/// руководитель. Анонимные подключения групп не имеют и ничего не получают.
/// </summary>
public class RealtimeNotifier(IHubContext<VacationHub> hub) : IRealtimeNotifier
{
    public Task NotifyLeaveCreatedAsync(LeaveRequestDto dto, CancellationToken ct = default)
    {
        var tasks = new List<Task>
        {
            hub.Clients.Group("u:" + dto.UserId).SendAsync("LeaveCreated", dto, ct)
        };
        if (dto.ManagerId is int managerId)
            tasks.Add(hub.Clients.Group("u:" + managerId).SendAsync("LeaveCreated", dto, ct));
        return Task.WhenAll(tasks);
    }

    public Task NotifyStatusChangedAsync(int requestId, string status, int ownerUserId, CancellationToken ct = default)
        => hub.Clients.Group("u:" + ownerUserId).SendAsync("LeaveStatusChanged", new { requestId, status }, ct);
}
