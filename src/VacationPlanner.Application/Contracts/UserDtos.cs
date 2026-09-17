namespace VacationPlanner.Application.Contracts;

/// <summary>
/// Публичное представление пользователя (без хеша пароля).
/// Role отдаём строкой ("Manager"/"Employee"), а не числом enum.
/// </summary>
public record UserDto(int Id, string Name, string Email, string Role);