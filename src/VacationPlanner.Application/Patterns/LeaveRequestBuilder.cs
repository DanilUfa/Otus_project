using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Application;

namespace VacationPlanner.Application.Patterns;

/// <summary>
/// PATTERN: Builder.
/// Пошаговая сборка заявки с проверками на каждом шаге и в Build().
/// Удобно для UI-формы и защищает от «недособранного» объекта.
/// </summary>
public class LeaveRequestBuilder
{
    private int _userId;
    private LeaveType _type = LeaveType.Annual;
    private DateTime _start;
    private DateTime _end;
    private string _reason = "";

    public LeaveRequestBuilder ForUser(int userId)
    {
        _userId = userId;
        return this;
    }

    public LeaveRequestBuilder OfType(LeaveType type)
    {
        _type = type;
        return this;
    }

    public LeaveRequestBuilder Between(DateTime start, DateTime end)
    {
        _start = start;
        _end = end;
        return this;
    }

    public LeaveRequestBuilder Reason(string reason)
    {
        _reason = reason ?? "";
        return this;
    }

    public LeaveRequest Build()
    {
        if (_start == default || _end == default)
            throw new DomainValidationException("Даты отпуска не заданы.");
        if (_end.Date < _start.Date)
            throw new DomainValidationException("Дата окончания раньше даты начала.");

        return LeaveRequestFactory.Create(_userId, _type, _start, _end, _reason);
    }
}