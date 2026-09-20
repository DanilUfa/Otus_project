using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Web.Health;

/// <summary>Readiness-проверка БД: выполняет SELECT 1.</summary>
public sealed class DbHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}

/// <summary>Readiness-проверка TCP-подключения (RabbitMQ/Redis).</summary>
public sealed class TcpHealthCheck(string host, int port, string name) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var client = new System.Net.Sockets.TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(2000);
            await client.ConnectAsync(host, port, timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{name} ({host}:{port}) недоступен: {ex.Message}");
        }
    }
}