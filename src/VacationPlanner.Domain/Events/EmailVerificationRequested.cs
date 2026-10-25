namespace VacationPlanner.Domain.Events;

/// <summary>
/// Событие «запрошен код подтверждения регистрации».
/// Notifier шлёт его письмом на почту пользователя (код живёт в Redis-кэше api с TTL).
/// </summary>
public record EmailVerificationRequested(
    string Email,
    string Code,
    string Name);
