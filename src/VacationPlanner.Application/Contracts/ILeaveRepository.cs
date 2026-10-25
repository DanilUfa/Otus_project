using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface ILeaveRepository
{
    Task<List<LeaveRequest>> GetAllAsync(CancellationToken ct = default);
    Task<LeaveRequest?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(LeaveRequest request, CancellationToken ct = default);
    Task<List<LeaveRequest>> GetByUserInRangeAsync(int userId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<List<LeaveRequest>> GetStaleDraftRequestsAsync(DateTime olderThan, CancellationToken ct = default);

    Task<List<LeaveAttachment>> GetAttachmentsAsync(int leaveId, CancellationToken ct = default);
    Task<LeaveAttachment?> GetAttachmentByIdAsync(int attachmentId, CancellationToken ct = default);
    Task AddAttachmentAsync(LeaveAttachment attachment, CancellationToken ct = default);
    Task DeleteAttachmentAsync(LeaveAttachment attachment, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}