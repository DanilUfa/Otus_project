using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
            Apply(existing, e);
        }
        else
        {
            var row = new ReadLeave { RequestId = e.RequestId, UserId = e.UserId, Status = LeaveStatus.Submitted };
            Apply(row, e);
            db.ReadLeaves.Add(row);
        }
        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Гонка: строка появилась параллельно (unique RequestId) — перечитываем и применяем.
            var row = await db.ReadLeaves.FirstOrDefaultAsync(r => r.RequestId == e.RequestId, context.CancellationToken);
            if (row is not null)
            {
                Apply(row, e);
                await db.SaveChangesAsync(context.CancellationToken);
            }
        }
        log.LogInformation("READMODEL requestId={RequestId} upsert status=Submitted userId={UserId}", e.RequestId, e.UserId);

        static void Apply(ReadLeave row, LeaveRequestSubmitted ev)
        {
            row.UserId = ev.UserId;
            row.UserName = ev.UserName;
            row.Type = ev.Type;
            row.StartDate = ev.StartDate;
            row.EndDate = ev.EndDate;
            row.Status = LeaveStatus.Submitted;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }
}
