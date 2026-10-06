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
    VacationMetrics metrics,
    IRealtimeNotifier realtime)
{
    public async Task<LeaveRequestDto> SubmitAsync(CreateLeaveRequestRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdWithManagerAsync(request.UserId, ct)
            ?? throw new InvalidOperationException($"Пользователь {request.UserId} не найден.");

        var leaveRequest = new LeaveRequestBuilder()
            .ForUser(request.UserId)
            .OfType(request.Type)
            .Between(request.StartDate, request.EndDate)
            .Reason(request.Reason ?? "")
            .Build();

        leaveRequest.Status = LeaveStatus.Submitted; // Draft → Submitted
        LeaveState.For(LeaveStatus.Draft).EnsureCanTransitionTo(leaveRequest.Status);

        await leaveRepository.AddAsync(leaveRequest, ct);
        await leaveRepository.SaveAsync(ct);

        await publishEndpoint.Publish(
            new LeaveRequestSubmitted(
                leaveRequest.Id,
                leaveRequest.UserId,
                user.Name,
                leaveRequest.Type,
                leaveRequest.StartDate,
                leaveRequest.EndDate,
                leaveRequest.CreatedAt,
                user.Email,
                user.Manager?.Email ?? ""),
            ct);

        metrics.LeavesSubmitted.Add(1);

        // Realtime: владельцу и руководителю (best-effort, не должно ломать создание).
        var dto = ToDto(leaveRequest, user.Name, user.ManagerId);
        try { await realtime.NotifyLeaveCreatedAsync(dto, ct); } catch { }

        return dto;
    }

    /// <summary>Отправляет сохранённый черновик (Draft → Submitted) на согласование.</summary>
    public async Task<LeaveRequestDto> SubmitDraftAsync(int id, int userId, CancellationToken ct = default)
    {
        var lr = await leaveRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Заявка {id} не найдена.");
        if (lr.UserId != userId)
            throw new UnauthorizedAccessException("Отправить черновик может только его автор.");

        // State: Draft → Submitted (нелегальные переходы запрещены).
        LeaveState.For(lr.Status).EnsureCanTransitionTo(LeaveStatus.Submitted);

        var owner = await userRepository.GetByIdWithManagerAsync(lr.UserId, ct);

        lr.Status = LeaveStatus.Submitted;
        await leaveRepository.SaveAsync(ct);

        await publishEndpoint.Publish(
            new LeaveRequestSubmitted(lr.Id, lr.UserId, owner?.Name ?? $"User {lr.UserId}",
                lr.Type, lr.StartDate, lr.EndDate, lr.CreatedAt,
                owner?.Email ?? "", owner?.Manager?.Email ?? ""),
            ct);
        metrics.LeavesSubmitted.Add(1);

        var dto = ToDto(lr, owner?.Name ?? $"User {lr.UserId}", owner?.ManagerId);
        try { await realtime.NotifyLeaveCreatedAsync(dto, ct); } catch { }

        return dto;
    }

    /// <summary>Отменяет заявку её автором (Draft → Cancelled, Submitted → Cancelled).</summary>
    public async Task CancelAsync(int id, int userId, CancellationToken ct = default)
    {
        var lr = await leaveRepository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Заявка {id} не найдена.");
        if (lr.UserId != userId)
            throw new UnauthorizedAccessException("Отменить может только автор заявки.");

        LeaveState.For(lr.Status).EnsureCanTransitionTo(LeaveStatus.Cancelled);
        lr.Status = LeaveStatus.Cancelled;
        await leaveRepository.SaveAsync(ct);

        // Отмена — тоже изменение статуса: уведомляем шину (reporting уберёт строку
        // из read-модели, audit запишет историю) и realtime-подписчиков.
        var owner2 = await userRepository.GetByIdAsync(lr.UserId, ct);
        await publishEndpoint.Publish(
            new LeaveRequestStatusChanged(lr.Id, lr.UserId, LeaveStatus.Cancelled,
                lr.StartDate, lr.EndDate, owner2?.Email ?? ""),
            ct);
        try { await realtime.NotifyStatusChangedAsync(lr.Id, LeaveStatus.Cancelled.ToString(), lr.UserId, ct); } catch { }
    }

    /// <summary>Сохраняет черновик (статус Draft): ни событий, ни метрик, ни realtime.</summary>
    public async Task<int> SaveDraftAsync(CreateLeaveRequestRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, ct)
            ?? throw new InvalidOperationException($"Пользователь {request.UserId} не найден.");

        var draft = new LeaveRequestBuilder()
            .ForUser(request.UserId)
            .OfType(request.Type)
            .Between(request.StartDate, request.EndDate)
            .Reason(request.Reason ?? "")
            .Build(); // исходное состояние — Draft

        await leaveRepository.AddAsync(draft, ct);
        await leaveRepository.SaveAsync(ct);
        return draft.Id;
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

        // State: запрещаем нелегальные переходы (например, утвердить уже отклонённую/не поданную).
        LeaveState.For(lr.Status).EnsureCanTransitionTo(newStatus);

        lr.Status = newStatus;
        await leaveRepository.SaveAsync(ct);

        if (newStatus == LeaveStatus.Approved) metrics.LeavesApproved.Add(1);
        else if (newStatus == LeaveStatus.Declined) metrics.LeavesDeclined.Add(1);

        // Уведомляем сотрудника об утверждении/отклонении (notifier шлёт email).
        await publishEndpoint.Publish(
            new LeaveRequestStatusChanged(
                lr.Id, lr.UserId, newStatus, lr.StartDate, lr.EndDate, owner.Email),
            ct);

        // Realtime: владельцу заявки (best-effort).
        try { await realtime.NotifyStatusChangedAsync(lr.Id, newStatus.ToString(), lr.UserId, ct); } catch { }
    }

    public async Task<List<LeaveRequestDto>> GetAllAsync(CancellationToken ct = default)
    {
        var users = await userRepository.GetAllAsync(ct);
        var names = users.ToDictionary(u => u.Id, u => u.Name);

        var requests = await leaveRepository.GetAllAsync(ct);
        return requests.Select(r => ToDto(r, names.GetValueOrDefault(r.UserId) ?? $"User {r.UserId}")).ToList();
    }

    /// <summary>Только заявки указанного пользователя (для сотрудника: видит свои).</summary>
    public async Task<List<LeaveRequestDto>> GetAllForUserAsync(int userId, CancellationToken ct = default)
    {
        var users = await userRepository.GetAllAsync(ct);
        var names = users.ToDictionary(u => u.Id, u => u.Name);

        var requests = await leaveRepository.GetAllAsync(ct);
        return requests
            .Where(r => r.UserId == userId)
            .Select(r => ToDto(r, names.GetValueOrDefault(r.UserId) ?? $"User {r.UserId}"))
            .ToList();
    }

    private static LeaveRequestDto ToDto(LeaveRequest r, string userName, int? managerId = null) => new(
        r.Id,
        r.UserId,
        userName,
        r.Type,
        r.StartDate,
        r.EndDate,
        r.Status,
        r.Reason,
        r.Days,
        managerId);
}