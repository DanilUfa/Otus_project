using Xunit;
using VacationPlanner.Application;
using VacationPlanner.Application.Patterns;
using VacationPlanner.Domain;

namespace VacationPlanner.UnitTests;

public class LeaveRequestBuilderTests
{
    private static DateTime Dt(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

    [Fact]
    public void End_before_start_throws_DomainValidation()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            new LeaveRequestBuilder().ForUser(1).OfType(LeaveType.Annual)
                .Between(Dt("2026-06-10"), Dt("2026-06-01")).Build());
        Assert.Contains("раньше", ex.Message);
    }

    [Fact]
    public void Missing_dates_throws_DomainValidation()
    {
        Assert.Throws<DomainValidationException>(() =>
            new LeaveRequestBuilder().ForUser(1).OfType(LeaveType.Annual).Build());
    }

    [Fact]
    public void Valid_period_builds_request()
    {
        var r = new LeaveRequestBuilder().ForUser(1).OfType(LeaveType.Annual)
            .Between(Dt("2026-06-01"), Dt("2026-06-05"))
            .Reason("test").Build();
        Assert.Equal(LeaveStatus.Draft, r.Status);
        Assert.Equal(5, r.Days);
    }
}
