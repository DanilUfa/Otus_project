using System.Net;
using System.Text;
using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Notifier.Consumers;

/// <summary>
/// Отправляет письмо с кодом подтверждения регистрации. Код хранится в Redis-кэше api
/// (см. RegistrationService) и приходит в событии — notifier остаётся stateless.
/// </summary>
public class EmailVerificationConsumer(
    INotificationSender notificationSender,
    ILogger<EmailVerificationConsumer> logger) : IConsumer<EmailVerificationRequested>
{
    public async Task Consume(ConsumeContext<EmailVerificationRequested> context)
    {
        var e = context.Message;
        if (string.IsNullOrWhiteSpace(e.Email))
        {
            logger.LogWarning("Событие подтверждения регистрации без email — письмо не отправлено.");
            return;
        }

        var subject = "Подтверждение регистрации — «Планирование отпусков»";
        var body = new StringBuilder()
            .Append("<h3>Здравствуйте, ").Append(WebUtility.HtmlEncode(e.Name)).Append("!</h3>")
            .Append("<p>Вы начали регистрацию в сервисе «Планирование отпусков». Ваш код подтверждения:</p>")
            .Append("<p style=&quot;font-size:28px;letter-spacing:6px;font-weight:700;color:#2563eb;&quot;>").Append(e.Code).Append("</p>")
            .Append("<p>Код действителен 10 минут. Если вы не регистрировались — просто проигнорируйте это письмо.</p>")
            .ToString();

        await notificationSender.SendAsync(e.Email, subject, body, context.CancellationToken);
    }
}
