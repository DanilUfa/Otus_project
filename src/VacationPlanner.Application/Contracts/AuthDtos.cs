namespace VacationPlanner.Application.Contracts;

public record LoginRequest(string Email, string Password);

/// <summary>Регистрация: запрос кода подтверждения на почту.</summary>
public record RegisterRequest(string Email, string Name, string Password);

/// <summary>Подтверждение регистрации кодом из письма.</summary>
public record ConfirmRegistrationRequest(string Email, string Code);

/// <summary>Повторная отправка кода подтверждения (не чаще 1 раза в минуту).</summary>
public record ResendRegistrationRequest(string Email);

/// <summary>Тело /auth/refresh и /auth/logout: сам refresh-токен (передаётся каждый раз).</summary>
public record RefreshRequest(string RefreshToken);

/// <summary>Ответ аутентификации: пара access+refresh. Access — JWT для Authorization header.</summary>
public record LoginResponse(
    string Token,
    string RefreshToken,
    int UserId,
    string Name,
    string Role);
