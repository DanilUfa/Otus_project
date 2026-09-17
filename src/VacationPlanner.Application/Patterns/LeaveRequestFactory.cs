using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Patterns;

/// <summary>
/// PATTERN: Factory Method.
/// Создаёт заявку нужного типа/статуса, скрывая детали инициализации
/// от вызывающего кода. Дальнейшие ветвления подтипов можно добавить здесь же.
/// </summary>
public static class LeaveRequestFactory
{
    public static LeaveRequest Create(
        int userId,
        LeaveType type,
        DateTime startDate,
        DateTime endDate,
        string reason) =>
        new()
        {
            UserId = userId,
            Type = type,
            StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc),
            Status = LeaveStatus.Draft,
            Reason = reason ?? "",
            CreatedAt = DateTime.UtcNow
        };
}