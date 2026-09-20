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

    public Task SaveAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}