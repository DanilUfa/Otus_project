using VacationPlanner.Domain;

namespace VacationPlanner.Application.Contracts;

/// <summary>Запрос на создание заявки.</summary>
public record CreateLeaveRequestRequest(
    int UserId,
    LeaveType Type,
    DateTime StartDate,
    DateTime EndDate,
    string? Reason);

/// <summary>Заявка для ответа API.</summary>
public record LeaveRequestDto(
    int Id,
    int UserId,
    string UserName,
    LeaveType Type,
    DateTime StartDate,
    DateTime EndDate,
    LeaveStatus Status,
    string Reason,
    int Days,
    int? ManagerId = null,
    int AttachmentCount = 0);

/// <summary>Документ, прикреплённый к заявке (метаданные, без содержимого).</summary>
public record LeaveAttachmentDto(
    int Id,
    int LeaveRequestId,
    string FileName,
    string ContentType,
    long Length,
    int UploadedByUserId,
    DateTime UploadedAt);

/// <summary>Готовый файл для скачивания.</summary>
public record LeaveAttachmentDownload(string FileName, string ContentType, byte[] Content);
