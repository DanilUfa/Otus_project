using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Audit.Consumers;

/// <summary>
/// Микросервис <b>audit</b>: фиксирует смену статуса в СВОЕЙ БД (история = Memento).
/// Идемпотентность: unique (RequestId, Action, Detail) — redelivery не дублирует запись.
/// </summary>
public class LeaveStatusAuditConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveStatusAuditConsumer> log) : IConsumer<LeaveRequestStatusChanged>
{
    public async Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var e = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var detail = e.Status.ToString();
        var exists = await db.AuditLogs.AnyAsync(
            a => a.RequestId == e.RequestId && a.Action == "status" && a.Detail == detail,
            context.CancellationToken);
        if (exists)
        {
            log.LogInformation("AUDIT requestId={RequestId} action=status — повтор события, пропуск", e.RequestId);
            return;
        }

        db.AuditLogs.Add(new AuditLog { RequestId = e.RequestId, UserId = e.UserId, Action = "status", Detail = detail });
        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            log.LogInformation("AUDIT requestId={RequestId} action=status — конкурентный дубль, пропуск", e.RequestId);
            return;
        }

        log.LogInformation("AUDIT requestId={RequestId} action=status userId={UserId} newStatus={Status}", e.RequestId, e.UserId, e.Status);
    }
}
