using System.Text;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Worker.Consumers;

/// <summary>
/// Обрабатывает событие «статус заявки изменён» (утверждена/отклонена):
/// уведомляет сотрудника по email. Best-effort: ошибка роняет только обработку сообщения.
/// (Observer/Mediator через MassTransit.)
/// </summary>
public class LeaveRequestStatusChangedConsumer(
    IUserRepository userRepository,
    INotificationSender notificationSender,
    ILogger<LeaveRequestStatusChangedConsumer> logger)
    : IConsumer<LeaveRequestStatusChanged>
{
    public async Task Consume(ConsumeContext<LeaveRequestStatusChanged> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var employee = await userRepository.GetByIdAsync(e.UserId, ct);
        if (employee is null)
        {
            logger.LogWarning("Сотрудник {UserId} не найден — уведомление о заявке {RequestId} не отправлено.", e.UserId, e.RequestId);
            return;
        }

        var verb = e.Status == LeaveStatus.Approved ? "утверждена" : "отклонена";
        var subject = $"Заявка на отпуск #{e.RequestId} {verb}";
        var body = new StringBuilder()
            .Append($"<h3>Ваш отпуск {(e.Status == LeaveStatus.Approved ? "утверждён" : "отклонён")}</h3>")
            .Append($"<p>Заявка #{e.RequestId}</p>")
            .Append($"<p>Статус: <b>{verb}</b></p>")
            .Append($"<p>Период: {e.StartDate:yyyy-MM-dd} .. {e.EndDate:yyyy-MM-dd}</p>")
            .Append("<p>С уважением, «Планирование отпусков».</p>")
            .ToString();

        await notificationSender.SendAsync(employee.Email, subject, body, ct);
    }
}