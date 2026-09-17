using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface ITokenService
{
    LoginResponse CreateToken(User user);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}