using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Audit.Consumers;

/// <summary>
/// Микросервис <b>audit</b>: фиксирует смену статуса заявки в своей таблице.
/// История статусов — основа бонус-паттерна «Memento»/audit trail.
/// </summary>
public class LeaveStatusAuditConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveStatusAuditConsumer> log) : IConsumer<LeaveRequestStatusChanged>
{
    public async Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var e = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.Add(new AuditLog { RequestId = e.RequestId, UserId = e.UserId, Action = "status", Detail = e.Status.ToString() });
        await db.SaveChangesAsync(context.CancellationToken);

        log.LogInformation("AUDIT requestId={RequestId} action=status userId={UserId} newStatus={Status}", e.RequestId, e.UserId, e.Status);
    }
}
