using Microsoft.EntityFrameworkCore;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Infrastructure.Repositories;

public class LeaveRepository(AppDbContext db) : ILeaveRepository
{
    public Task<List<LeaveRequest>> GetAllAsync(CancellationToken ct = default)
        => db.LeaveRequests.AsNoTracking().OrderBy(r => r.StartDate).ToListAsync(ct);

    public Task<LeaveRequest?> GetByIdAsync(int id, CancellationToken ct = default)
        => db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task AddAsync(LeaveRequest request, CancellationToken ct = default)
    {
        db.LeaveRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task<List<LeaveRequest>> GetByUserInRangeAsync(
        int userId, DateTime from, DateTime to, CancellationToken ct = default)
        => db.LeaveRequests
            .Where(r => r.UserId == userId && r.StartDate <= to && r.EndDate >= from)
            .ToListAsync(ct);

    public Task<List<LeaveRequest>> GetStaleDraftRequestsAsync(DateTime olderThan, CancellationToken ct = default)
        => db.LeaveRequests
            .Where(r => r.Status == LeaveStatus.Draft && r.CreatedAt < olderThan)
            .ToListAsync(ct);

    public Task<List<LeaveAttachment>> GetAttachmentsAsync(int leaveId, CancellationToken ct = default)
        => db.LeaveAttachments.AsNoTracking()
            .Where(a => a.LeaveRequestId == leaveId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync(ct);

    public Task<LeaveAttachment?> GetAttachmentByIdAsync(int attachmentId, CancellationToken ct = default)
        => db.LeaveAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct);

    public Task AddAttachmentAsync(LeaveAttachment attachment, CancellationToken ct = default)
    {
        db.LeaveAttachments.Add(attachment);
        return Task.CompletedTask;
    }

    public Task DeleteAttachmentAsync(LeaveAttachment attachment, CancellationToken ct = default)
    {
        db.LeaveAttachments.Remove(attachment);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public async Task<Dictionary<int, int>> GetAttachmentCountsAsync(IEnumerable<int> leaveIds, CancellationToken ct = default)
    {
        var ids = leaveIds.ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();
        return await db.LeaveAttachments
            .Where(a => ids.Contains(a.LeaveRequestId))
            .GroupBy(a => a.LeaveRequestId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }
}