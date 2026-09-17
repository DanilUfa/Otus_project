using System.Text;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Worker.Consumers;

/// <summary>
/// Обрабатывает событие «заявка подана»:
/// - пересчитывает остаток дней под async-замком (SemaphoreSlim) — защита от гонок;
/// - уведомляет руководителя по email (Outlook/SMTP).
/// Всё best-effort: ошибки роняют только обработку сообщения, не весь worker.
/// (Observer/Mediator через MassTransit.)
/// </summary>
public class LeaveRequestSubmittedConsumer(
    IUserRepository userRepository,
    INotificationSender notificationSender,
    BalanceService balanceService,
    BalanceGate balanceGate,
    ILogger<LeaveRequestSubmittedConsumer> logger)
    : IConsumer<LeaveRequestSubmitted>
{
    public async Task Consume(ConsumeContext<LeaveRequestSubmitted> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        logger.LogInformation(
            "Обработка заявки {RequestId}: пользователь {UserId}, {Type}, {Start}..{End}",
            e.RequestId, e.UserId, e.Type, e.StartDate, e.EndDate);

        // ---- 1. Пересчёт баланса (под per-user SemaphoreSlim) ----
        int remaining;
        using (await balanceGate.LockAsync(e.UserId, ct))
        {
            remaining = await balanceService.GetRemainingAnnualDaysAsync(e.UserId, e.StartDate.Year, ct);
        }
        logger.LogInformation("Пользователь {UserId}: остаток годового отпуска на {Year} = {Days} дн.",
            e.UserId, e.StartDate.Year, remaining);

        // ---- 2. Уведомление руководителя ----
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