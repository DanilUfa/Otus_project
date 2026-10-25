using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>
/// In-memory реализация репозитория. GetByUserInRangeAsync повторяет SQL-семантику
/// реального LeaveRepository (пересечение окна: StartDate <= to && EndDate >= from),
/// чтобы unit-тест проверял поведение сервиса + контракт репозитория вместе.
/// </summary>
public sealed class FakeLeaveRepository : ILeaveRepository
{
    public List<LeaveRequest> Items { get; } = new();
    public List<LeaveAttachment> AttachmentItems { get; } = new();
    private int _nextAttachmentId = 1;
    public int InRangeCalls { get; private set; }
    public (int UserId, DateTime From, DateTime To)? LastRange { get; private set; }

    public Task<List<LeaveRequest>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(Items.ToList());

    public Task<LeaveRequest?> GetByIdAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));

    public Task AddAsync(LeaveRequest request, CancellationToken ct = default)
    {
        Items.Add(request);
        return Task.CompletedTask;
    }

    public Task<List<LeaveRequest>> GetByUserInRangeAsync(
        int userId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        InRangeCalls++;
        LastRange = (userId, from, to);
        return Task.FromResult(Items
            .Where(r => r.UserId == userId && r.StartDate <= to && r.EndDate >= from)
            .ToList());
    }

    public Task<List<LeaveRequest>> GetStaleDraftRequestsAsync(DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult(Items
            .Where(r => r.Status == LeaveStatus.Draft && r.CreatedAt < olderThan)
            .ToList());

    public Task<List<LeaveAttachment>> GetAttachmentsAsync(int leaveId, CancellationToken ct = default)
        => Task.FromResult(AttachmentItems.Where(a => a.LeaveRequestId == leaveId).ToList());

    public Task<LeaveAttachment?> GetAttachmentByIdAsync(int attachmentId, CancellationToken ct = default)
        => Task.FromResult(AttachmentItems.FirstOrDefault(a => a.Id == attachmentId));

    public Task AddAttachmentAsync(LeaveAttachment attachment, CancellationToken ct = default)
    {
        if (attachment.Id == 0) attachment.Id = _nextAttachmentId++;
        AttachmentItems.Add(attachment);
        return Task.CompletedTask;
    }

    public Task DeleteAttachmentAsync(LeaveAttachment attachment, CancellationToken ct = default)
    {
        AttachmentItems.Remove(attachment);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
}
