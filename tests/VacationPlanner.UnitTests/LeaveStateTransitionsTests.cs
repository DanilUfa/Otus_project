using Xunit;
using VacationPlanner.Application.Patterns;
using VacationPlanner.Domain;

namespace VacationPlanner.UnitTests;

/// <summary>
/// State: полная матрица переходов жизненного цикла заявки.
/// Документированная матрица:
///   Draft      -> Submitted, Cancelled
///   Submitted  -> Approved, Declined, Cancelled
///   Approved   -> Cancelled
///   Declined   -> Submitted
///   Cancelled  -> (терминальное)
/// </summary>
public class LeaveStateTransitionsTests
{
    public static TheoryData<LeaveStatus, LeaveStatus[]> AllowedMatrix => new()
    {
        { LeaveStatus.Draft,     new[] { LeaveStatus.Submitted, LeaveStatus.Cancelled } },
        { LeaveStatus.Submitted, new[] { LeaveStatus.Approved, LeaveStatus.Declined, LeaveStatus.Cancelled } },
        { LeaveStatus.Approved,  new[] { LeaveStatus.Cancelled } },
        { LeaveStatus.Declined,  new[] { LeaveStatus.Submitted } },
        { LeaveStatus.Cancelled, Array.Empty<LeaveStatus>() },
    };

    [Theory]
    [MemberData(nameof(AllowedMatrix))]
    public void AllowedTransitions_match_documented_matrix(LeaveStatus from, LeaveStatus[] allowed)
    {
        var state = LeaveState.For(from);
        Assert.Equal(allowed.OrderBy(x => x), state.AllowedTransitions.OrderBy(x => x));
        foreach (var next in allowed)
            state.EnsureCanTransitionTo(next); // не должно бросить
    }

    [Theory]
    [MemberData(nameof(AllowedMatrix))]
    public void Forbidden_transitions_throw(LeaveStatus from, LeaveStatus[] allowed)
    {
        var forbidden = Enum.GetValues<LeaveStatus>().Except(allowed).ToArray();
        var state = LeaveState.For(from);
        foreach (var next in forbidden)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => state.EnsureCanTransitionTo(next));
            Assert.Contains("не может перейти", ex.Message);
        }
    }

    [Fact]
    public void Factory_throws_on_unknown_status()
        => Assert.Throws<ArgumentOutOfRangeException>(() => LeaveState.For((LeaveStatus)99));

    [Theory]
    [MemberData(nameof(AllowedMatrix))]
    public void Status_property_reflects_the_state(LeaveStatus expected, LeaveStatus[] _)
        => Assert.Equal(expected, LeaveState.For(expected).Status);
}
