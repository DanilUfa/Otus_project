namespace VacationPlanner.Application.Contracts;

/// <summary>
/// Контракт realtime-оповещений (реализуется на SignalR в Web, а в приложении —
/// только интерфейс). Лучшее-effort: сбой уведомления не должен ломать запрос.
/// </summary>
public interface IRealtimeNotifier
{
    Task NotifyLeaveCreatedAsync(LeaveRequestDto dto, CancellationToken ct = default);
    Task NotifyStatusChangedAsync(int requestId, string status, CancellationToken ct = default);
}