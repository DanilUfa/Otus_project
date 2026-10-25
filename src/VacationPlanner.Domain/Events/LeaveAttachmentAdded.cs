namespace VacationPlanner.Domain.Events;

/// <summary>
/// Событие «к заявке прикреплён документ». Публикуется api после сохранения
/// файла (та же транзакция через outbox); потребитель — audit (запись истории).
/// </summary>
public record LeaveAttachmentAdded(
    int RequestId,
    int AttachmentId,
    string FileName,
    long Length,
    int UploadedByUserId);
