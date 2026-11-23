namespace VacationPlanner.Domain.Events;

/// <summary>
/// Событие «статус заявки изменился» (утверждена / отклонена / отменена).
/// Несёт email сотрудника — notifier уведомляет без обращения к БД.
/// </summary>
public record LeaveRequestStatusChanged(
    int RequestId,
    int UserId,
    LeaveStatus Status,
    DateTime StartDate,
    DateTime EndDate,
    string EmployeeEmail,
    long Revision = 0,
    string UserName = "",
    LeaveType? Type = null);
