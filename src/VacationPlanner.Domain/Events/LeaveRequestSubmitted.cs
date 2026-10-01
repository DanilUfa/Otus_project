namespace VacationPlanner.Domain.Events;

/// <summary>
/// Событие «заявка подана». Публикуется api после сохранения.
/// Обогащено данными отправителя: read/notification-сервисы не ходят в БД api.
/// </summary>
public record LeaveRequestSubmitted(
    int RequestId,
    int UserId,
    string UserName,
    LeaveType Type,
    DateTime StartDate,
    DateTime EndDate,
    DateTime CreatedAt,
    string EmployeeEmail,
    string ManagerEmail);
