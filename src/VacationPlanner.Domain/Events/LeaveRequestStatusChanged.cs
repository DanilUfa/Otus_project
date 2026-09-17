namespace VacationPlanner.Domain.Events;

/// <summary>
/// Событие «статус заявки изменился» (утверждена / отклонена).
/// Публикуется API после смены статуса руководителем; Worker уведомляет сотрудника по email.
/// </summary>
public record LeaveRequestStatusChanged(
    int RequestId,
    int UserId,
    LeaveStatus Status,
    DateTime StartDate,
    DateTime EndDate);