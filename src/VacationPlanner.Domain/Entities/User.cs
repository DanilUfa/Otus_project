namespace VacationPlanner.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Employee;

    /// <summary>Хэш пароля (SHA-256). Черновик; позже заменим на ASP.NET Identity/Keycloak.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Руководитель пользователя (self-reference).</summary>
    public int? ManagerId { get; set; }
    public User? Manager { get; set; }

    public List<LeaveRequest> LeaveRequests { get; set; } = new();
}