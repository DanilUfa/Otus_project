using VacationPlanner.Domain;

namespace VacationPlanner.Application.Contracts;

/// <summary>Запрос на создание заявки.</summary>
public record CreateLeaveRequestRequest(
    int UserId,
    LeaveType Type,
    DateTime StartDate,
    DateTime EndDate,
    string? Reason);

/// <summary>Заявка для ответа API.</summary>
public record LeaveRequestDto(
    int Id,
    int UserId,
    string UserName,
    LeaveType Type,
    DateTime StartDate,
    DateTime EndDate,
    LeaveStatus Status,
    string Reason,
    int Days);