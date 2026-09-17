namespace VacationPlanner.Domain;

/// <summary>Тип отпуска.</summary>
public enum LeaveType
{
    Annual,
    Sick,
    Unpaid
}

/// <summary>Статус заявки (жизненный цикл).</summary>
public enum LeaveStatus
{
    Draft,
    Submitted,
    Approved,
    Declined,
    Cancelled
}

/// <summary>Роль пользователя.</summary>
public enum UserRole
{
    Employee,
    Manager,
    Admin
}