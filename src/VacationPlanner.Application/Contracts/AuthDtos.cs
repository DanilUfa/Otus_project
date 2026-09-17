namespace VacationPlanner.Application.Contracts;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string Token,
    int UserId,
    string Name,
    string Role);