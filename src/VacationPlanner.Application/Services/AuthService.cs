using VacationPlanner.Application.Contracts;

namespace VacationPlanner.Application.Services;

public class AuthService(IUserRepository userRepository, IPasswordHasher passwordHasher, ITokenService tokenService)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct)
            ?? throw new UnauthorizedAccessException("Неверный email или пароль.");

        if (string.IsNullOrEmpty(request.Password) || !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Неверный email или пароль.");

        return tokenService.CreateToken(user);
    }
}