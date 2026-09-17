using VacationPlanner.Domain;

namespace VacationPlanner.Application.Patterns;

/// <summary>
/// PATTERN: State.
/// Жизненный цикл заявки описывается объектами-состояниями: каждое состояние знает,
/// в какие состояния из него допустим переход. Нелегальные переходы запрещаются (throw).
/// </summary>
public abstract class LeaveState
{
    /// <summary>Текущий статус.</summary>
    public abstract LeaveStatus Status { get; }

    /// <summary>Список допустимых переходов из этого состояния.</summary>
    public abstract IReadOnlyList<LeaveStatus> AllowedTransitions { get; }

    /// <summary>Проверяет возможность перехода и кидает исключение при запрещённом.</summary>
    public void EnsureCanTransitionTo(LeaveStatus next)
    {
        if (!AllowedTransitions.Contains(next))
            throw new InvalidOperationException(
                $"Заявка в состоянии '{Status}' не может перейти в '{next}'.");
    }

    /// <summary>Фабричный метод: возвращает нужное состояние по статусу.</summary>
    public static LeaveState For(LeaveStatus status) => status switch
    {
        LeaveStatus.Draft => new DraftState(),
        LeaveStatus.Submitted => new SubmittedState(),
        LeaveStatus.Approved => new ApprovedState(),
        LeaveStatus.Declined => new DeclinedState(),
        LeaveStatus.Cancelled => new CancelledState(),
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}

/// <summary>Черновик → можно отправить на согласование или отменить.</summary>
public sealed class DraftState : LeaveState
{
    public override LeaveStatus Status => LeaveStatus.Draft;
    public override IReadOnlyList<LeaveStatus> AllowedTransitions =>
        [LeaveStatus.Submitted, LeaveStatus.Cancelled];
}

/// <summary>На согласовании → утвердить, отклонить или отменить.</summary>
public sealed class SubmittedState : LeaveState
{
    public override LeaveStatus Status => LeaveStatus.Submitted;
    public override IReadOnlyList<LeaveStatus> AllowedTransitions =>
        [LeaveStatus.Approved, LeaveStatus.Declined, LeaveStatus.Cancelled];
}

/// <summary>Утверждена → только отмена.</summary>
public sealed class ApprovedState : LeaveState
{
    public override LeaveStatus Status => LeaveStatus.Approved;
    public override IReadOnlyList<LeaveStatus> AllowedTransitions => [LeaveStatus.Cancelled];
}

/// <summary>Отклонена → можно подать вновь.</summary>
public sealed class DeclinedState : LeaveState
{
    public override LeaveStatus Status => LeaveStatus.Declined;
    public override IReadOnlyList<LeaveStatus> AllowedTransitions => [LeaveStatus.Submitted];
}

/// <summary>Отменена → терминальное состояние.</summary>
public sealed class CancelledState : LeaveState
{
    public override LeaveStatus Status => LeaveStatus.Cancelled;
    public override IReadOnlyList<LeaveStatus> AllowedTransitions => [];
}