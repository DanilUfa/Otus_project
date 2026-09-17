using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using VacationPlanner.Application.Contracts;

namespace VacationPlanner.Infrastructure.Notifications;

/// <summary>
/// Уведомления по email через SMTP (Outlook / Microsoft 365).
/// Если SMTP не настроен — отправка пропускается (логируется), приложение не падает.
/// </summary>
public class EmailNotificationSender(IConfiguration configuration, ILogger<EmailNotificationSender> logger)
    : INotificationSender
{
    public async Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken ct = default)
    {
        var s = configuration.GetSection("Notifications:Email");

        var host = s["Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            logger.LogInformation("SMTP не настроен — уведомление {Subject} не отправлено (адрес: {To}).", subject, toEmail);
            return;
        }

        var port = int.TryParse(s["Port"], out var p) ? p : 587;
        var useSsl = bool.TryParse(s["UseSsl"], out var ssl) ? ssl : false;
        var startTls = bool.TryParse(s["StartTls"], out var st) ? st : false;

        var mode = useSsl ? SecureSocketOptions.SslOnConnect
                : startTls ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, mode, ct);

            var username = s["Username"];
            var password = s["Password"];
            if (!string.IsNullOrWhiteSpace(username))
                await client.AuthenticateAsync(username!, password!, ct);

            // Outlook требует, чтобы From совпадал с авторизованным ящиком,
            // иначе письмо не проходит. Если From не задан — берём Username.
            var fromRaw = s["From"];
            if (string.IsNullOrWhiteSpace(fromRaw) && !string.IsNullOrWhiteSpace(username))
                fromRaw = username;
            fromRaw = string.IsNullOrWhiteSpace(fromRaw) ? "no-reply@vac.local" : fromRaw;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Планирование отпусков", fromRaw!));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = bodyHtml };

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Уведомление отправлено на {To}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось отправить уведомление на {To} ({Subject}).", toEmail, subject);
        }
    }
}