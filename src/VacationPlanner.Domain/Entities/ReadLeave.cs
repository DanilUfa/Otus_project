using VacationPlanner.Domain;

namespace VacationPlanner.Domain.Entities;

/// <summary>
/// Read-модель (CQRS): денормализованная проекция заявки для отчётов/календаря.
/// Ведётся микросервисом <c>reporting</c> из событий шины (event-driven read model).
/// </summary>
public class ReadLeave
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public LeaveType Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public LeaveStatus Status { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int Days => (EndDate.Date - StartDate.Date).Days + 1;
}
