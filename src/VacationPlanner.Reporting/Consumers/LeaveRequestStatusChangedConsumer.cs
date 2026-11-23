using MassTransit;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Reporting.Consumers;

public class LeaveRequestStatusChangedConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveRequestStatusChangedConsumer> log) : IConsumer<LeaveRequestStatusChanged>
{
    public async Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var message = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await CalendarProjection.ApplyAsync(db, message, context.CancellationToken);
        log.LogInformation("READMODEL requestId={RequestId} revision={Revision}", message.RequestId, message.Revision);
        // Also republish on duplicate delivery: an earlier attempt may have committed before publish failed.
        await context.Publish(new CalendarUpdated(message.StartDate.Year, message.EndDate.Year), context.CancellationToken);
    }
}
