using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

/// <summary>Канал realtime (SignalR). Application знает только интерфейс.</summary>
public interface IRealtimeNotifier
{
    /// <summary>Новая заявка: уведомляем владельца и (если есть) руководителя.</summary>
    Task NotifyLeaveCreatedAsync(LeaveRequestDto dto, CancellationToken ct = default);

    /// <summary>Смена статуса: уведомляем владельца заявки.</summary>
    Task NotifyStatusChangedAsync(int requestId, string status, int ownerUserId, CancellationToken ct = default);
}
