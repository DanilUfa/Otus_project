using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Reporting.Consumers;

/// <summary>
/// Микросервис <b>reporting</b>: заявка подана → upsert read-модели в СВОЕЙ БД.
/// Имя/email приходят в событии — сервис не обращается к БД api.
/// </summary>
public class LeaveRequestSubmittedConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveRequestSubmittedConsumer> log) : IConsumer<LeaveRequestSubmitted>
{
    public async Task Consume(ConsumeContext<LeaveRequestSubmitted> context)
    {
        var e = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        var existing = await db.ReadLeaves.FirstOrDefaultAsync(r => r.RequestId == e.RequestId, context.CancellationToken);
        if (existing is not null)
        {
            existing.UserName = e.UserName;
            existing.Type = e.Type;
            existing.StartDate = e.StartDate;
            existing.EndDate = e.EndDate;
            existing.Status = LeaveStatus.Submitted;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.ReadLeaves.Add(new ReadLeave
            {
                RequestId = e.RequestId,
                UserId = e.UserId,
                UserName = e.UserName,
                Type = e.Type,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                Status = LeaveStatus.Submitted
            });
        }
        await db.SaveChangesAsync(context.CancellationToken);
        log.LogInformation("READMODEL requestId={RequestId} upsert status=Submitted userId={UserId}", e.RequestId, e.UserId);
    }
}
