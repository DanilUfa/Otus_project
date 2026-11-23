using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using Xunit;

namespace VacationPlanner.UnitTests;

public class ReliableEventsTests
{
    private static readonly DateTime Start = new(2028, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static LeaveRequestSubmitted Submitted(long revision) => new(42, 2, "Employee", LeaveType.Sick,
        Start, Start.AddDays(2), Start, "employee@test.local", "manager@test.local", revision);
    private static LeaveRequestStatusChanged Changed(LeaveStatus status, long revision) => new(42, 2, status,
        Start, Start.AddDays(2), "employee@test.local", revision, "Employee", LeaveType.Sick);

    [Fact]
    public void Duplicate_create_cannot_reset_approval_or_resurrect_cancelled_leave()
    {
        var row = new ReadLeave();
        Assert.True(row.Apply(Submitted(1)));
        Assert.True(row.Apply(Changed(LeaveStatus.Approved, 2)));
        Assert.False(row.Apply(Submitted(1)));
        Assert.Equal(LeaveStatus.Approved, row.Status);
        Assert.True(row.Apply(Changed(LeaveStatus.Cancelled, 3)));
        Assert.False(row.Apply(Changed(LeaveStatus.Approved, 2)));
        Assert.False(row.Apply(Submitted(1)));
        Assert.Equal(LeaveStatus.Cancelled, row.Status);
        Assert.Equal(3, row.Revision);
    }

    [Fact]
    public void Status_before_create_has_complete_metadata_and_newer_resubmission_is_applied()
    {
        var row = new ReadLeave();
        Assert.True(row.Apply(Changed(LeaveStatus.Declined, 2)));
        Assert.False(row.Apply(Submitted(1)));
        Assert.Equal("Employee", row.UserName);
        Assert.Equal(LeaveType.Sick, row.Type);
        Assert.True(row.Apply(Submitted(3)));
        Assert.Equal(LeaveStatus.Submitted, row.Status);
    }

    [Fact]
    public void Legacy_cancelled_tombstone_accepts_metadata_without_restoring_leave()
    {
        var row = new ReadLeave();
        row.Apply(new LeaveRequestStatusChanged(42, 2, LeaveStatus.Cancelled, Start, Start, "e@test.local"));
        Assert.True(row.Apply(Submitted(0)));
        Assert.Equal(LeaveStatus.Cancelled, row.Status);
        Assert.Equal("Employee", row.UserName);
        Assert.False(row.Apply(Changed(LeaveStatus.Approved, 0)));
    }

    [Fact]
    public void Outbox_keeps_retrying_after_five_failures_with_bounded_delay()
    {
        var message = new OutboxMessage { Failed = true, FailedCount = 5 };
        var now = DateTime.UtcNow;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            message.ScheduleRetry(now);
            Assert.False(message.Failed);
            Assert.Null(message.PublishedAt);
            Assert.InRange((message.NextAttemptAt!.Value - now).TotalSeconds, 1, 300);
        }
        Assert.Equal(25, message.FailedCount);
    }
}
