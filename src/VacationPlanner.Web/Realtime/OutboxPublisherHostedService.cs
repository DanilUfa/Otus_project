using System.Text.Json;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Web.Realtime;

/// <summary>
/// Отправитель transaction outbox: каждые Outbox:IntervalSeconds (по умолчанию 2 с)
/// выбирает неопубликованные сообщения по порядку и публикует их в шину.
/// Гарантия at-least-once: если процесс упал после публикации, но до отметки PublishedAt,
/// событие будет опубликовано повторно — консьюмеры идемпотентны (unique-ключи).
/// </summary>
public class OutboxPublisherHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxPublisherHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = int.TryParse(configuration["Outbox:IntervalSeconds"], out var s) && s > 0 ? s : 2;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        log.LogInformation("OUTBOX sender запущен, интервал={Seconds}с", seconds);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "OUTBOX ошибка пакетной публикации");
            }
        }
    }

    private async Task PublishBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        const int batchSize = 50;
        var pending = await outbox.GetPendingAsync(batchSize, ct);
        if (pending.Count == 0) return;

        foreach (var m in pending)
        {
            try
            {
                object evt = m.Type switch
                {
                    nameof(LeaveRequestSubmitted) => JsonSerializer.Deserialize<LeaveRequestSubmitted>(m.Payload)!,
                    nameof(LeaveRequestStatusChanged) => JsonSerializer.Deserialize<LeaveRequestStatusChanged>(m.Payload)!,
                    nameof(LeaveAttachmentAdded) => JsonSerializer.Deserialize<LeaveAttachmentAdded>(m.Payload)!,
                    nameof(EmailVerificationRequested) => JsonSerializer.Deserialize<EmailVerificationRequested>(m.Payload)!,
                    _ => throw new InvalidOperationException($"Неизвестный тип outbox: {m.Type}")
                };
                await publishEndpoint.Publish(evt, ct);
                m.PublishedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                m.FailedCount++;
                if (m.FailedCount >= 5)
                {
                    m.Failed = true;
                    log.LogError(ex, "OUTBOX {Id} {Type}: {N} неудачных попыток, сообщение помечено Failed", m.Id, m.Type, m.FailedCount);
                }
                else
                {
                    log.LogWarning(ex, "OUTBOX {Id} {Type}: публикация не удалась (попытка {N} из 5)", m.Id, m.Type, m.FailedCount);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("OUTBOX опубликовано {Count} событий", pending.Count);
    }
}
