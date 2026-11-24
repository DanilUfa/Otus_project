using System.Text.Json;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Metrics;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.UnitTests.Fakes;
using Xunit;

namespace VacationPlanner.UnitTests;

public class LeaveApprovalTests
{
    [Theory]
    [InlineData(1, 1, LeaveStatus.Approved)]
    [InlineData(1, 1, LeaveStatus.Declined)]
    [InlineData(3, 1, LeaveStatus.Approved)]
    [InlineData(3, 1, LeaveStatus.Declined)]
    public async Task Manager_can_decide_own_or_direct_report_leave(int ownerId, int actorId, LeaveStatus status)
    {
        var h = new Harness(ownerId);
        await h.Service.UpdateStatusAsync(42, status, actorId);

        Assert.Equal(status, h.Leave.Status);
        Assert.Equal(2, h.Leave.Revision);
        var message = Assert.Single(h.Outbox.Items);
        var changed = JsonSerializer.Deserialize<LeaveRequestStatusChanged>(message.Payload)!;
        Assert.Equal(ownerId, changed.UserId);
        Assert.Equal(status, changed.Status);
        Assert.Equal(2, changed.Revision);
        Assert.Equal(1, h.Realtime.StatusNotifications);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    public async Task Employee_self_approval_and_unrelated_manager_are_rejected(int ownerId, int actorId)
    {
        var h = new Harness(ownerId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            h.Service.UpdateStatusAsync(42, LeaveStatus.Approved, actorId));

        Assert.Equal(LeaveStatus.Submitted, h.Leave.Status);
        Assert.Equal(1, h.Leave.Revision);
        Assert.Empty(h.Outbox.Items);
        Assert.Equal(0, h.Realtime.StatusNotifications);
    }

    [Fact]
    public async Task Manager_self_approval_still_requires_submitted_state()
    {
        var h = new Harness(1);
        h.Leave.Status = LeaveStatus.Draft;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.UpdateStatusAsync(42, LeaveStatus.Approved, 1));
        Assert.Equal(LeaveStatus.Draft, h.Leave.Status);
        Assert.Empty(h.Outbox.Items);
    }

    private sealed class Harness
    {
        public LeaveRequest Leave { get; }
        public FakeOutboxRepository Outbox { get; } = new();
        public RecordingRealtime Realtime { get; } = new();
        public LeaveService Service { get; }

        public Harness(int ownerId)
        {
            var users = new FakeUserRepository();
            users.Users.Add(new User { Id = 1, Role = UserRole.Manager, Name = "Manager", Email = "manager@test.local" });
            users.Users.Add(new User { Id = 2, Role = UserRole.Manager, Name = "Other manager", Email = "other@test.local" });
            users.Users.Add(new User { Id = 3, Role = UserRole.Employee, ManagerId = 1, Name = "Employee", Email = "employee@test.local" });
            var repo = new FakeLeaveRepository();
            Leave = new LeaveRequest { Id = 42, UserId = ownerId, Type = LeaveType.Annual,
                Status = LeaveStatus.Submitted, Revision = 1,
                StartDate = new DateTime(2035, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2035, 6, 2, 0, 0, 0, DateTimeKind.Utc) };
            repo.Items.Add(Leave);
            var gate = new BalanceGate();
            Service = new LeaveService(repo, users, Outbox, new FakeUnitOfWork(), new VacationMetrics(),
                Realtime, new BalanceService(repo, gate), gate);
        }
    }

    private sealed class RecordingRealtime : IRealtimeNotifier
    {
        public int StatusNotifications { get; private set; }
        public Task NotifyLeaveCreatedAsync(LeaveRequestDto dto, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyStatusChangedAsync(int requestId, string status, int ownerUserId, CancellationToken ct = default)
        {
            StatusNotifications++;
            return Task.CompletedTask;
        }
    }
}
