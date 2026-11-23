namespace VacationPlanner.Domain.Entities;

/// <summary>
/// Transaction outbox: событие, которое нужно опубликовать в шину.
/// Пишется В ТОЙ ЖЕ транзакции, что и изменение агрегата (заявки), фоновый
/// отправитель публикует его и помечает PublishedAt. Гарантия: либо заявка
/// сохранена вместе со событием, либо ничего не сохранено.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    /// <summary>Имя доменного события (LeaveRequestSubmitted / LeaveRequestStatusChanged).</summary>
    public string Type { get; set; } = "";
    /// <summary>JSON представление события.</summary>
    public string Payload { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public int FailedCount { get; set; }
    public bool Failed { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    public void ScheduleRetry(DateTime now)
    {
        FailedCount++;
        Failed = false; // including legacy Failed rows: failures never permanently disable delivery
        NextAttemptAt = now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(FailedCount, 9))));
    }
}
