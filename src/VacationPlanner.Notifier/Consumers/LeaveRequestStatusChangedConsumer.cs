using System.Text;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Notifier.Consumers;

/// <summary>
/// Микросервис <b>notifier</b>: уведомляет сотрудника об утверждении/отклонении/отмене.
/// Email сотрудника приходит в событии — без обращения к БД.
/// </summary>
public class LeaveRequestStatusChangedConsumer(
    INotificationSender notificationSender,
    ILogger<LeaveRequestStatusChangedConsumer> logger) : IConsumer<LeaveRequestStatusChanged>
{
    public async Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var e = context.Message;
        if (string.IsNullOrWhiteSpace(e.EmployeeEmail))
        {
            logger.LogWarning("Событие статуса заявки {RequestId} без email сотрудника.", e.RequestId);
            return;
        }

        var verb = e.Status switch
        {
            LeaveStatus.Approved => "утверждена",
            LeaveStatus.Declined => "отклонена",
            LeaveStatus.Cancelled => "отменена",
            _ => "изменена"
        };
        var subject = $"Заявка на отпуск #{e.RequestId} {verb}";
        var body = new StringBuilder()
            .Append($"<h3>Ваш отпуск {verb}</h3>")
            .Append($"<p>Заявка #{e.RequestId}</p>")
            .Append($"<p>Статус: <b>{verb}</b></p>")
            .Append($"<p>Период: {e.StartDate:yyyy-MM-dd} .. {e.EndDate:yyyy-MM-dd}</p>")
            .Append("<p>С уважением, «Планирование отпусков».</p>")
            .ToString();

        await notificationSender.SendAsync(e.EmployeeEmail, subject, body, context.CancellationToken);
    }
}
