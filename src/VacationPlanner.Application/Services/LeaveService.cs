using MassTransit;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Metrics;
using VacationPlanner.Application.Patterns;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Application.Services;

/// <summary>
/// Юз-кейсы работы с заявками на отпуск.
/// Сохраняет заявку и публикует событие в шину (MassTransit) для фоновой обработки.
/// </summary>
public class LeaveService(
    ILeaveRepository leaveRepository,
    IUserRepository userRepository,
    IPublishEndpoint publishEndpoint,
    VacationMetrics metrics)
{
    public async Task<LeaveRequestDto> SubmitAsync(CreateLeaveRequestRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, ct)
            ?? throw new InvalidOperationException($"Пользователь {request.UserId} не найден.");

        var leaveRequest = new LeaveRequestBuilder()
            .ForUser(request.UserId)
            .OfType(request.Type)
            .Between(request.StartDate, request.EndDate)
            .Reason(request.Reason ?? "")
            .Build();

        leaveRequest.Status = LeaveStatus.Submitted; // → впечатление: Draft → Submitted

        await leaveRepository.AddAsync(leaveRequest, ct);
        await leaveRepository.SaveAsync(ct);

        await publishEndpoint.Publish(
            new LeaveRequestSubmitted(
                leaveRequest.Id,
                leaveRequest.UserId,
                leaveRequest.Type,
                leaveRequest.StartDate,
                leaveRequest.EndDate,
                leaveRequest.CreatedAt),
            ct);

        metrics.LeavesSubmitted.Add(1);

        return ToDto(leaveRequest, user.Name);
    }

    public async Task UpdateStatusAsync(int id, LeaveStatus newStatus, int approverUserId, CancellationToken ct = default)
    {
        var lr = await leaveRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Заявка {id} не найдена.");

        var owner = await userRepository.GetByIdAsync(lr.UserId, ct)
            ?? throw new InvalidOperationException($"Сотрудник заявки {id} не найден.");

        // Только непосредственный руководитель сотрудника утверждает/отклоняет.
        if (owner.ManagerId != approverUserId)
            throw new UnauthorizedAccessException("Заявку может утвердить руководитель сотрудника.");

        lr.Status = newStatus;
        await leaveRepository.SaveAsync(ct);

        if (newStatus == LeaveStatus.Approved) metrics.LeavesApproved.Add(1);
        else if (newStatus == LeaveStatus.Declined) metrics.LeavesDeclined.Add(1);

        // Уведомляем сотрудника об утверждении/отклонении (Worker шлёт email).
        await publishEndpoint.Publish(
            new LeaveRequestStatusChanged(
                lr.Id, lr.UserId, newStatus, lr.StartDate, lr.EndDate),
            ct);
    }

    public async Task<List<LeaveRequestDto>> GetAllAsync(CancellationToken ct = default)
    {
        var users = await userRepository.GetAllAsync(ct);
        var names = users.ToDictionary(u => u.Id, u => u.Name);

        var requests = await leaveRepository.GetAllAsync(ct);
        return requests.Select(r => ToDto(r, names.GetValueOrDefault(r.UserId) ?? $"User {r.UserId}")).ToList();
    }

    private static LeaveRequestDto ToDto(LeaveRequest r, string userName) => new(
        r.Id,
        r.UserId,
        userName,
        r.Type,
        r.StartDate,
        r.EndDate,
        r.Status,
        r.Reason,
        r.Days);
}