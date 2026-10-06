namespace VacationPlanner.Application.Contracts;

public record LoginRequest(string Email, string Password);

/// <summary>Тело /auth/refresh и /auth/logout: сам refresh-токен (передаётся каждый раз).</summary>
public record RefreshRequest(string RefreshToken);

/// <summary>Ответ аутентификации: пара access+refresh. Access — JWT для Authorization header.</summary>
public record LoginResponse(
    string Token,
    string RefreshToken,
    int UserId,
    string Name,
    string Role);
