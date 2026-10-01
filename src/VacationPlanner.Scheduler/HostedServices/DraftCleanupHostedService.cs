using VacationPlanner.Application.Services;

namespace VacationPlanner.Scheduler.HostedServices;

public sealed class DraftCleanupOptions
{
    public int IntervalSeconds { get; set; } = 20;
    public int StaleDays { get; set; } = 3;
}

/// <summary>
/// Микросервис <b>scheduler</b>: периодически закрывает «зависшие» черновики
/// (Draft → Cancelled) через <see cref="StaleDraftService"/> (State-переходы).
/// </summary>
public sealed class DraftCleanupHostedService(
    ILogger<DraftCleanupHostedService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = config.GetSection("DraftCleanup").Get<DraftCleanupOptions>() ?? new DraftCleanupOptions();
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.IntervalSeconds));
        var staleAge = TimeSpan.FromDays(options.StaleDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<StaleDraftService>();
                var cutoff = DateTime.UtcNow - staleAge;
                var closed = await service.CancelStaleDraftsAsync(cutoff, stoppingToken);
                if (closed > 0)
                    logger.LogInformation("Автополитика: закрыто «зависших» черновиков: {count}", closed);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ошибка авто-закрытия черновиков"); }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
