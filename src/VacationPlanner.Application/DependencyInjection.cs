using Microsoft.Extensions.DependencyInjection;
using VacationPlanner.Application.Metrics;
using VacationPlanner.Application.Services;

namespace VacationPlanner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<LeaveService>();
        services.AddScoped<BalanceService>();
        services.AddScoped<AuthService>();
        services.AddSingleton<BalanceGate>();
        services.AddSingleton<VacationMetrics>();
        return services;
    }
}