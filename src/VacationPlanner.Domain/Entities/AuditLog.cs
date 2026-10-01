namespace VacationPlanner.Domain.Entities;

/// <summary>
/// Аудит-запись: кто/когда что сделал с заявкой.
/// Bounded-контекст аудита ведётся отдельным микросервисом <c>audit</c>.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; } = "";  // 'created' | 'status'
    public string? Detail { get; set; }       // напр. "Submitted" / "Approved" / "Declined"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
