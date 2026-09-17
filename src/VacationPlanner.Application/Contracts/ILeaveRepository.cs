using VacationPlanner.Domain.Entities;

namespace VacationPlanner.Application.Contracts;

public interface ILeaveRepository
{
    Task<List<LeaveRequest>> GetAllAsync(CancellationToken ct = default);
    Task<LeaveRequest?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(LeaveRequest request, CancellationToken ct = default);
    Task<List<LeaveRequest>> GetByUserInRangeAsync(int userId, DateTime from, DateTime to, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}