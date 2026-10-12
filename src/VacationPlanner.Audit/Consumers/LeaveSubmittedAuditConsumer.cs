using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Audit.Consumers;

/// <summary>
/// Микросервис <b>audit</b>: фиксирует создание заявки в СВОЕЙ БД (AuditLogs).
/// Идемпотентность: at-least-once доставка шины + unique (RequestId, Action, Detail) —
/// повторная обработка одного события (redelivery после падения ДО ack) не дублирует запись.
/// </summary>
public class LeaveSubmittedAuditConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveSubmittedAuditConsumer> log) : IConsumer<LeaveRequestSubmitted>
{
    public async Task Consume(ConsumeContext<LeaveRequestSubmitted> context)
    {
        var e = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var detail = e.Type.ToString();
        var exists = await db.AuditLogs.AnyAsync(
            a => a.RequestId == e.RequestId && a.Action == "created" && a.Detail == detail,
            context.CancellationToken);
        if (exists)
        {
            log.LogInformation("AUDIT requestId={RequestId} action=created — повтор события, пропуск", e.RequestId);
            return;
        }

        db.AuditLogs.Add(new AuditLog { RequestId = e.RequestId, UserId = e.UserId, Action = "created", Detail = detail });
        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            log.LogInformation("AUDIT requestId={RequestId} action=created — конкурентный дубль, пропуск", e.RequestId);
            return;
        }

        log.LogInformation("AUDIT requestId={RequestId} action=created userId={UserId} type={Type}", e.RequestId, e.UserId, e.Type);
    }
}
