namespace VacationPlanner.Domain.Events;

/// <summary>
/// Событие «заявка подана». Публикуется приложением (API) в шину MassTransit,
/// обрабатывается фоновым Worker'ом (Observer/Mediator как следствие MassTransit).
/// </summary>
public record LeaveRequestSubmitted(
    int RequestId,
    int UserId,
    LeaveType Type,
    DateTime StartDate,
    DateTime EndDate,
    DateTime CreatedAt);