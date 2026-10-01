using System.Text;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Notifier.Consumers;

/// <summary>
/// Микросервис <b>notifier</b>: уведомляет руководителя о новой заявке по email.
/// Только уведомления (наблюдатель/медиатор через MassTransit).
/// </summary>
public class LeaveRequestSubmittedConsumer(
    IUserRepository userRepository,
    INotificationSender notificationSender,
    ILogger<LeaveRequestSubmittedConsumer> logger)
    : IConsumer<LeaveRequestSubmitted>
{
    public async Task Consume(ConsumeContext<LeaveRequestSubmitted> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var employee = await userRepository.GetByIdWithManagerAsync(e.UserId, ct);
        if (employee is null || employee.Manager is null)
        {
            logger.LogWarning("У сотрудника {UserId} нет руководителя — уведомление не отправлено.", e.UserId);
            return;
        }

        var manager = employee.Manager;
        var subject = $"Новая заявка на отпуск от {employee.Name}";
        var body = new StringBuilder()
            .Append("<h3>Новая заявка на отпуск</h3>")
            .Append($"<p>Сотрудник: <b>{employee.Name}</b> ({employee.Email})</p>")
            .Append($"<p>Тип: {e.Type}<br/>Период: {e.StartDate:yyyy-MM-dd} .. {e.EndDate:yyyy-MM-dd}</p>")
            .Append($"<p>Заявка #{e.RequestId} ожидает вашего согласования.</p>")
            .ToString();

        await notificationSender.SendAsync(manager.Email, subject, body, ct);
    }
}
