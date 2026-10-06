using Xunit;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.UnitTests.Fakes;

namespace VacationPlanner.UnitTests;

/// <summary>
/// Расчёт остатка: только Approved + Annual, дни включительно
/// (Days = (EndDate - StartDate).Days + 1), результат клампится к 0.
/// Окно года (overlap) отдаёт репозиторий — фабрики проверяют границы.
/// </summary>
public class BalanceServiceTests
{
    private static DateTime Dt(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

    private static LeaveRequest Leaf(int id, int userId, LeaveStatus status, LeaveType type, string from, string to)
        => new()
        {
            Id = id,
            UserId = userId,
            Status = status,
            Type = type,
            StartDate = Dt(from),
            EndDate = Dt(to),
            CreatedAt = Dt(from),
        };

    private static (FakeLeaveRepository Repo, BalanceService Svc) New()
    {
        var repo = new FakeLeaveRepository();
        return (repo, new BalanceService(repo, new BalanceGate()));
    }

    [Fact]
    public async Task No_requests_returns_full_quota()
    {
        var (_, svc) = New();
        Assert.Equal(BalanceService.DefaultAnnualDays, await svc.GetRemainingAnnualDaysAsync(1, 2026));
    }

    [Fact]
    public async Task Only_approved_annual_leaves_reduce_balance()
    {
        var (repo, svc) = New();
        repo.Items.AddRange(new[]
        {
            Leaf(1, 1, LeaveStatus.Draft,     LeaveType.Annual, "2026-06-01", "2026-06-05"),
            Leaf(2, 1, LeaveStatus.Submitted, LeaveType.Annual, "2026-06-01", "2026-06-05"),
            Leaf(3, 1, LeaveStatus.Declined,  LeaveType.Annual, "2026-06-01", "2026-06-05"),
            Leaf(4, 1, LeaveStatus.Cancelled, LeaveType.Annual, "2026-06-01", "2026-06-05"),
            Leaf(5, 1, LeaveStatus.Approved,  LeaveType.Sick,   "2026-06-01", "2026-06-05"),
            Leaf(6, 1, LeaveStatus.Approved,  LeaveType.Unpaid, "2026-06-01", "2026-06-05"),
            Leaf(7, 1, LeaveStatus.Approved,  LeaveType.Annual, "2026-08-03", "2026-08-10"), // 8 дн
        });
        Assert.Equal(BalanceService.DefaultAnnualDays - 8, await svc.GetRemainingAnnualDaysAsync(1, 2026));
    }

    [Fact]
    public async Task Days_are_inclusive()
    {
        var (repo, svc) = New();
        repo.Items.Add(Leaf(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-06-01", "2026-06-21"));
        Assert.Equal(BalanceService.DefaultAnnualDays - 21, await svc.GetRemainingAnnualDaysAsync(1, 2026));
    }

    [Fact]
    public async Task Sums_multiple_approved_leaves()
    {
        var (repo, svc) = New();
        repo.Items.AddRange(new[]
        {
            Leaf(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-06-01", "2026-06-07"),
            Leaf(2, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-09-01", "2026-09-07"),
        });
        Assert.Equal(BalanceService.DefaultAnnualDays - 14, await svc.GetRemainingAnnualDaysAsync(1, 2026));
    }

    [Fact]
    public async Task Balance_is_clamped_at_zero()
    {
        var (repo, svc) = New();
        repo.Items.Add(Leaf(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-01-01", "2026-01-30"));
        Assert.Equal(0, await svc.GetRemainingAnnualDaysAsync(1, 2026));
    }

    [Fact]
    public async Task Passes_full_year_window_to_repository()
    {
        var (repo, svc) = New();
        await svc.GetRemainingAnnualDaysAsync(42, 2027);
        Assert.Equal(
            (42, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                 new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)),
            repo.LastRange);
    }

    [Fact]
    public async Task Leaf_outside_the_year_window_is_not_counted()
    {
        var (repo, svc) = New();
        repo.Items.AddRange(new[]
        {
            Leaf(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2025-11-01", "2025-11-05"),
            Leaf(2, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-06-01", "2026-06-05"),
        });
        Assert.Equal(BalanceService.DefaultAnnualDays - 5, await svc.GetRemainingAnnualDaysAsync(1, 2026));
    }

    [Fact]
    public async Task Leaf_spanning_year_boundary_is_counted_in_both_years()
    {
        var (repo, svc) = New();
        repo.Items.Add(Leaf(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-12-30", "2027-01-02")); // 4 дня
        Assert.Equal(BalanceService.DefaultAnnualDays - 4, await svc.GetRemainingAnnualDaysAsync(1, 2026));
        Assert.Equal(BalanceService.DefaultAnnualDays - 4, await svc.GetRemainingAnnualDaysAsync(1, 2027));
    }
}
