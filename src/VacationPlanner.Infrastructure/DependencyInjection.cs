using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Infrastructure.Notifications;
using VacationPlanner.Infrastructure.Repositories;
using VacationPlanner.Infrastructure.Security;

namespace VacationPlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=vacation;Username=vac;Password=vac";

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        services.AddScoped<ILeaveRepository, LeaveRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Security
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        // Notifications
        services.AddScoped<INotificationSender, EmailNotificationSender>();

        return services;
    }
}