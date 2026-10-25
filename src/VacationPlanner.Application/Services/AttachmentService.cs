using System.Text.Json;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Документы, прикреплённые к заявке (больничный лист, справка и т.п.).
/// Файл хранится в БД api; факт прикрепления уходит в шину событием
/// LeaveAttachmentAdded (аудит фиксирует историю). Загрузка — только автором
/// заявки; чтение/скачивание/удаление — автором или руководителем/админом.
/// Разрешённые типы и лимит размера проверяются до записи.
/// </summary>
public class AttachmentService(
    ILeaveRepository leaves,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork)
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg"
    };

    public async Task<LeaveAttachmentDto> AddAsync(
        int leaveId, int uploaderUserId, string? fileName, string? contentType,
        long length, Stream content, CancellationToken ct = default)
    {
        var leave = await leaves.GetByIdAsync(leaveId, ct)
            ?? throw new NotFoundException($"Заявка {leaveId} не найдена.");
        if (leave.UserId != uploaderUserId)
            throw new UnauthorizedAccessException("Прикреплять документ может только автор заявки.");

        var name = string.IsNullOrWhiteSpace(fileName) ? "file" : Path.GetFileName(fileName);
        var ext = Path.GetExtension(name);
        if (!AllowedExtensions.Contains(ext))
            throw new DomainValidationException($"Недопустимый тип файла: {ext}. Разрешены: pdf, png, jpg, jpeg.");
        if (length <= 0 || length > MaxUploadBytes)
            throw new DomainValidationException(
                $"Файл пуст или больше {MaxUploadBytes / 1024 / 1024} МБ.");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        if (ms.Length <= 0)
            throw new DomainValidationException("Файл пуст.");

        var attachment = new LeaveAttachment
        {
            LeaveRequestId = leaveId,
            FileName = name,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Length = ms.Length,
            Content = ms.ToArray(),
            UploadedByUserId = uploaderUserId
        };

        await unitOfWork.BeginAsync(ct);
        try
        {
            await leaves.AddAttachmentAsync(attachment, ct);
            await leaves.SaveAsync(ct); // Id присваивается в транзакции

            // Событие строится с настоящим AttachmentId и кладётся в outbox той же транзакцией.
            await outbox.AddAsync(NewOutbox(new LeaveAttachmentAdded(
                leave.Id, attachment.Id, attachment.FileName, attachment.Length, uploaderUserId)), ct);
            await leaves.SaveAsync(ct);

            await unitOfWork.CommitAsync(ct);
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }

        return ToDto(attachment);
    }

    public async Task<List<LeaveAttachmentDto>> GetAttachmentsAsync(
        int leaveId, int userId, bool isManagerOrAdmin, CancellationToken ct = default)
    {
        var leave = await leaves.GetByIdAsync(leaveId, ct)
            ?? throw new NotFoundException($"Заявка {leaveId} не найдена.");
        if (!isManagerOrAdmin && leave.UserId != userId)
            throw new UnauthorizedAccessException("Список документов доступен автору заявки или руководителю.");

        var list = await leaves.GetAttachmentsAsync(leaveId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<LeaveAttachmentDownload> DownloadAsync(
        int attachmentId, int userId, bool isManagerOrAdmin, CancellationToken ct = default)
    {
        var attachment = await leaves.GetAttachmentByIdAsync(attachmentId, ct)
            ?? throw new NotFoundException($"Документ {attachmentId} не найден.");
        var leave = await leaves.GetByIdAsync(attachment.LeaveRequestId, ct)
            ?? throw new NotFoundException($"Заявка {attachment.LeaveRequestId} не найдена.");
        if (!isManagerOrAdmin && leave.UserId != userId)
            throw new UnauthorizedAccessException("Скачивание доступно автору заявки или руководителю.");

        return new LeaveAttachmentDownload(attachment.FileName, attachment.ContentType, attachment.Content);
    }

    public async Task DeleteAsync(
        int attachmentId, int userId, bool isManagerOrAdmin, CancellationToken ct = default)
    {
        var attachment = await leaves.GetAttachmentByIdAsync(attachmentId, ct)
            ?? throw new NotFoundException($"Документ {attachmentId} не найден.");
        var leave = await leaves.GetByIdAsync(attachment.LeaveRequestId, ct)
            ?? throw new NotFoundException($"Заявка {attachment.LeaveRequestId} не найдена.");
        if (!isManagerOrAdmin && leave.UserId != userId)
            throw new UnauthorizedAccessException("Удаление доступно автору заявки или руководителю.");

        await leaves.DeleteAttachmentAsync(attachment, ct);
        await leaves.SaveAsync(ct);
    }

    private static OutboxMessage NewOutbox(object evt) => new()
    {
        Type = evt.GetType().Name,
        Payload = JsonSerializer.Serialize(evt),
    };

    private static LeaveAttachmentDto ToDto(LeaveAttachment a) => new(
        a.Id,
        a.LeaveRequestId,
        a.FileName,
        a.ContentType,
        a.Length,
        a.UploadedByUserId,
        a.UploadedAt);
}