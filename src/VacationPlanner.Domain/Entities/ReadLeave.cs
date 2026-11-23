using VacationPlanner.Domain;
using VacationPlanner.Domain.Events;

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
    public long Revision { get; set; }

    public int Days => (EndDate.Date - StartDate.Date).Days + 1;

    public bool Apply(LeaveRequestSubmitted e)
        => Apply(e.RequestId, e.UserId, e.UserName, e.Type, e.StartDate, e.EndDate,
            LeaveStatus.Submitted, e.Revision, isSubmission: true);

    public bool Apply(LeaveRequestStatusChanged e)
        => Apply(e.RequestId, e.UserId, e.UserName, e.Type, e.StartDate, e.EndDate,
            e.Status, e.Revision, isSubmission: false);

    private bool Apply(int requestId, int userId, string name, LeaveType? type, DateTime start, DateTime end,
        LeaveStatus status, long revision, bool isSubmission)
    {
        var advance = UserId == 0 || revision > Revision ||
            (revision == 0 && Revision == 0 &&
                ((Status == LeaveStatus.Submitted && status is LeaveStatus.Approved or LeaveStatus.Declined or LeaveStatus.Cancelled) ||
                 (Status == LeaveStatus.Approved && status == LeaveStatus.Cancelled)));
        // Legacy status events lacked descriptive fields. Fill them without regressing status.
        var fillMetadata = isSubmission && string.IsNullOrEmpty(UserName);
        if (!advance && !fillMetadata) return false;
        if (advance)
        {
            RequestId = requestId;
            UserId = userId;
            StartDate = start;
            EndDate = end;
            Status = status;
            Revision = revision;
        }
        if (!string.IsNullOrEmpty(name)) UserName = name;
        if (type is { } knownType) Type = knownType;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }
}
