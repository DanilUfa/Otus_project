using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Reporting.Consumers;

/// <summary>
/// Микросервис <b>reporting</b>: статус изменён → обновляем/гасим read-модель в СВОЕЙ БД.
/// </summary>
public class LeaveRequestStatusChangedConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveRequestStatusChangedConsumer> log) : IConsumer<LeaveRequestStatusChanged>
{
    public async Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var e = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        var row = await db.ReadLeaves.FirstOrDefaultAsync(r => r.RequestId == e.RequestId, context.CancellationToken);
        if (row is null) return;

        if (e.Status == LeaveStatus.Declined || e.Status == LeaveStatus.Cancelled)
        {
            db.ReadLeaves.Remove(row);
        }
        else
        {
            row.Status = e.Status;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(context.CancellationToken);
        log.LogInformation("READMODEL requestId={RequestId} status->{Status}", e.RequestId, e.Status);
    }
}
