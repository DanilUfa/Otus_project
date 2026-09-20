using MassTransit;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Worker.Consumers;

/// <summary>Аудит-консьюмер смены статуса: фиксирует, кем/во что изменена заявка.</summary>
public class LeaveStatusAuditConsumer(ILogger<LeaveStatusAuditConsumer> log)
    : IConsumer<LeaveRequestStatusChanged>
{
    public Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var e = context.Message;
        log.LogInformation(
            "AUDIT requestId={RequestId} action=status userId={UserId} newStatus={Status}",
            e.RequestId, e.UserId, e.Status);
        return Task.CompletedTask;
    }
}