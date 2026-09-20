using MassTransit;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Worker.Consumers;

/// <summary>
/// Второй консьюмер на то же событие (fan-out): пишет строку аудита.
/// RabbitMQ доставляет копию события и в основной консьюмер, и в этот.
/// </summary>
public class LeaveSubmittedAuditConsumer(ILogger<LeaveSubmittedAuditConsumer> log)
    : IConsumer<LeaveRequestSubmitted>
{
    public Task Consume(ConsumeContext<LeaveRequestSubmitted> context)
    {
        var e = context.Message;
        log.LogInformation(
            "AUDIT requestId={RequestId} action=created userId={UserId} type={Type} from={Start:yyyy-MM-dd} to={End:yyyy-MM-dd}",
            e.RequestId, e.UserId, e.Type, e.StartDate, e.EndDate);
        return Task.CompletedTask;
    }
}