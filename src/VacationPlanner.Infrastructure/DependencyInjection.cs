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
    /// <summary>Write-сторона (api / scheduler): Users + LeaveRequests.</summary>
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

        return services;
    }

    /// <summary>Аудит-хранилище (audit): собственная БД <c>vacation_audit</c>.</summary>
    public static IServiceCollection AddAuditStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("Audit")
                ?? throw new InvalidOperationException("ConnectionStrings:Audit не задана.")));
        return services;
    }

    /// <summary>Read-модель (reporting): собственная БД <c>vacation_reporting</c>.</summary>
    public static IServiceCollection AddReportingStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ReportingDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("Reporting")
                ?? throw new InvalidOperationException("ConnectionStrings:Reporting не задана.")));
        return services;
    }

    /// <summary>Только уведомления (notifier) — сервис без собственной БД.</summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddScoped<INotificationSender, EmailNotificationSender>();
        return services;
    }
}
