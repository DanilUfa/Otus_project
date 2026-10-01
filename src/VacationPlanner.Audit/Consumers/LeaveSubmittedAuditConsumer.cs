using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Audit.Consumers;

/// <summary>
/// Микросервис <b>audit</b>: фиксирует создание заявки в СВОЕЙ БД (AuditLogs).
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

        db.AuditLogs.Add(new AuditLog { RequestId = e.RequestId, UserId = e.UserId, Action = "created", Detail = e.Type.ToString() });
        await db.SaveChangesAsync(context.CancellationToken);

        log.LogInformation("AUDIT requestId={RequestId} action=created userId={UserId} type={Type}", e.RequestId, e.UserId, e.Type);
    }
}
