namespace VacationPlanner.Domain.Entities;

/// <summary>
/// Документ, прикреплённый к заявке на отпуск (больничный лист, справка и т.п.).
/// Хранится в БД api (bytea) — без внешнего blob-хранилища.
/// Метаданные уходят в шину событием LeaveAttachmentAdded (аудит).
/// </summary>
public class LeaveAttachment
{
    public int Id { get; set; }
    public int LeaveRequestId { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }

    /// <summary>Оригинальное имя файла (для UI и скачивания).</summary>
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public byte[] Content { get; set; } = [];

    public int UploadedByUserId { get; set; }
    public User? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
