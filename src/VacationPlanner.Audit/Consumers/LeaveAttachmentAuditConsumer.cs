using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;

namespace VacationPlanner.Audit.Consumers;

/// <summary>
/// Микросервис <b>audit</b>: фиксирует прикрепление документа к заявке в СВОЕЙ БД.
/// Detail содержит AttachmentId — unique (RequestId, Action, Detail) не даёт
/// задвоить запись при redelivery (повторная загрузка файла = новый AttachmentId).
/// </summary>
public class LeaveAttachmentAuditConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaveAttachmentAuditConsumer> log) : IConsumer<LeaveAttachmentAdded>
{
    public async Task Consume(ConsumeContext<LeaveAttachmentAdded> context)
    {
        var e = context.Message;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var detail = $"{e.AttachmentId}:{e.FileName} ({e.Length} б)";
        var exists = await db.AuditLogs.AnyAsync(
            a => a.RequestId == e.RequestId && a.Action == "attachment" && a.Detail == detail,
            context.CancellationToken);
        if (exists)
        {
            log.LogInformation("AUDIT requestId={RequestId} action=attachment — повтор события, пропуск", e.RequestId);
            return;
        }

        db.AuditLogs.Add(new AuditLog { RequestId = e.RequestId, UserId = e.UploadedByUserId, Action = "attachment", Detail = detail });
        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            log.LogInformation("AUDIT requestId={RequestId} action=attachment — конкурентный дубль, пропуск", e.RequestId);
            return;
        }

        log.LogInformation("AUDIT requestId={RequestId} action=attachment attachmentId={AttachmentId} file={FileName} user={UserId}",
            e.RequestId, e.AttachmentId, e.FileName, e.UploadedByUserId);
    }
}
