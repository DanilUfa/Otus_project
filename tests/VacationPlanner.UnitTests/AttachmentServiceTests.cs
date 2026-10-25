using Xunit;
using VacationPlanner.Application;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.UnitTests.Fakes;

namespace VacationPlanner.UnitTests;

/// <summary>
/// Прикрепление документов к заявке (больничный лист/справка): только автор может
/// загружать; разрешены pdf/png/jpg/jpeg до 10 МБ; скачивание/удаление — автор или
/// руководитель/админ; факт прикрепления уходит в outbox событием LeaveAttachmentAdded.
/// </summary>
public class AttachmentServiceTests
{
    private static DateTime Dt(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

    private static (FakeLeaveRepository Repo, FakeOutboxRepository Outbox, AttachmentService Svc) New()
    {
        var repo = new FakeLeaveRepository();
        repo.Items.Add(new LeaveRequest
        {
            Id = 1,
            UserId = 3, // Мария
            Type = LeaveType.Sick,
            Status = LeaveStatus.Submitted,
            StartDate = Dt("2026-11-09"),
            EndDate = Dt("2026-11-10")
        });
        var outbox = new FakeOutboxRepository();
        return (repo, outbox, new AttachmentService(repo, outbox, new FakeUnitOfWork()));
    }

    private static byte[] Bytes(int n) => Enumerable.Range(0, n).Select(i => (byte)i).ToArray();

    [Fact]
    public async Task Owner_can_attach_pdf_and_event_goes_to_outbox()
    {
        var (_, outbox, svc) = New();
        var content = Bytes(128);
        using var stream = new MemoryStream(content);

        var dto = await svc.AddAsync(1, 3, "bolnichnyi.pdf", "application/pdf", content.Length, stream);

        Assert.Equal(1, dto.LeaveRequestId);
        Assert.Equal("bolnichnyi.pdf", dto.FileName);
        Assert.Equal(content.Length, dto.Length);
        Assert.Equal(3, dto.UploadedByUserId);

        var evt = Assert.Single(outbox.Items);
        Assert.Equal("LeaveAttachmentAdded", evt.Type);
        Assert.Contains("\"RequestId\":1", evt.Payload);
        Assert.Contains("\"FileName\":\"bolnichnyi.pdf\"", evt.Payload);
    }

    [Fact]
    public async Task Non_owner_cannot_attach()
    {
        var (_, _, svc) = New();
        using var stream = new MemoryStream(Bytes(16));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AddAsync(1, 4, "doc.pdf", "application/pdf", 16, stream));
    }

    [Fact]
    public async Task Disallowed_extension_rejected()
    {
        var (_, _, svc) = New();
        using var stream = new MemoryStream(Bytes(16));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => svc.AddAsync(1, 3, "virus.exe", "application/x-msdownload", 16, stream));
    }

    [Fact]
    public async Task Oversized_file_rejected()
    {
        var (_, _, svc) = New();
        using var stream = new MemoryStream(Bytes(16));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => svc.AddAsync(1, 3, "big.pdf", "application/pdf", AttachmentService.MaxUploadBytes + 1, stream));
    }

    [Fact]
    public async Task List_hidden_from_another_employee_but_visible_to_manager()
    {
        var (repo, _, svc) = New();
        repo.AttachmentItems.Add(new LeaveAttachment { Id = 10, LeaveRequestId = 1, FileName = "spravka.pdf", Length = 5 });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.GetAttachmentsAsync(1, 4, false)); // Пётр
        var list = await svc.GetAttachmentsAsync(1, 3, false); // автор
        Assert.Single(list);
        var mgrList = await svc.GetAttachmentsAsync(1, 1, true); // руководитель
        Assert.Single(mgrList);
    }

    [Fact]
    public async Task Delete_by_other_employee_forbidden()
    {
        var (repo, _, svc) = New();
        repo.AttachmentItems.Add(new LeaveAttachment { Id = 10, LeaveRequestId = 1, FileName = "spravka.pdf", Length = 5 });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.DeleteAsync(10, 4, false, CancellationToken.None));
        await svc.DeleteAsync(10, 3, false, CancellationToken.None); // автор — можно
        Assert.Empty(repo.AttachmentItems);
    }
}