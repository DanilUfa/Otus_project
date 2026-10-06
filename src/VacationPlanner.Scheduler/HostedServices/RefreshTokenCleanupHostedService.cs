using VacationPlanner.Application.Services;

namespace VacationPlanner.Scheduler.HostedServices;

public sealed class RefreshTokenCleanupOptions
{
    public int IntervalSeconds { get; set; } = 300;
    public int CleanupGraceDays { get; set; } = 1;
}

/// <summary>
/// Микросервис <b>scheduler</b>: периодически удаляет недействительные refresh-токены
/// (истёкшие и отозванные за пределами grace-периода) через
/// <see cref="RefreshTokenCleanupService"/>. Раз в 5 минут по умолчанию.
/// </summary>
public sealed class RefreshTokenCleanupHostedService(
    ILogger<RefreshTokenCleanupHostedService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = config.GetSection("RefreshTokenCleanup").Get<RefreshTokenCleanupOptions>() ?? new RefreshTokenCleanupOptions();
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupService>();
                var deleted = await service.CleanupAsync(stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Очистка refresh-токенов: удалено {count}", deleted);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ошибка очистки refresh-токенов"); }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
