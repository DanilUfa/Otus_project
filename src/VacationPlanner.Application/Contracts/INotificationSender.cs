namespace VacationPlanner.Application.Contracts;

/// <summary>
/// Отправка уведомлений (email и др.). Реализация инфраструктурная (SMTP/Outlook/MailKit).
/// </summary>
public interface INotificationSender
{
    Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken ct = default);
}