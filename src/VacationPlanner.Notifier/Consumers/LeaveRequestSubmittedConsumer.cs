using System.Text;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Notifier.Consumers;

/// <summary>
/// Микросервис <b>notifier</b>: уведомляет руководителя о новой заявке.
/// Данные (имя, email) приходят ВМЕСТЕ с событием — консьюмер не ходит в БД.
/// </summary>
public class LeaveRequestSubmittedConsumer(
    INotificationSender notificationSender,
    ILogger<LeaveRequestSubmittedConsumer> logger) : IConsumer<LeaveRequestSubmitted>
{
    public async Task Consume(ConsumeContext<LeaveRequestSubmitted> context)
    {
        var e = context.Message;
        if (string.IsNullOrWhiteSpace(e.ManagerEmail))
        {
            logger.LogWarning("У сотрудника {UserId} нет руководителя — уведомление не отправлено.", e.UserId);
            return;
        }

        var subject = $"Новая заявка на отпуск от {e.UserName}";
        var body = new StringBuilder()
            .Append("<h3>Новая заявка на отпуск</h3>")
            .Append($"<p>Сотрудник: <b>{e.UserName}</b> ({e.EmployeeEmail})</p>")
            .Append($"<p>Тип: {e.Type}<br/>Период: {e.StartDate:yyyy-MM-dd} .. {e.EndDate:yyyy-MM-dd}</p>")
            .Append($"<p>Заявка #{e.RequestId} ожидает вашего согласования.</p>")
            .ToString();

        await notificationSender.SendAsync(e.ManagerEmail, subject, body, context.CancellationToken);
    }
}
