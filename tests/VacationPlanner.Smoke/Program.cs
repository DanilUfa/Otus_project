// Офлайн-двойник UnitTests: те же сценарии без внешних пакетов (nuget.org недоступен в этой
// сети). Реальный каркас: tests/VacationPlanner.UnitTests (xUnit).
using VacationPlanner.Application.Patterns;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;

var failures = 0;

void Ok(string name, bool cond, string? detail = null)
{
    if (cond) Console.WriteLine("  ok    " + name);
    else { failures++; Console.WriteLine("  FAIL  " + name + (detail is null ? "" : " :: " + detail)); }
}

void Throws<T>(string name, Action act) where T : Exception
{
    try { act(); failures++; Console.WriteLine("  FAIL  " + name + " :: ожидалось " + typeof(T).Name + ", исключения нет"); }
    catch (T) { Console.WriteLine("  ok    " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("  FAIL  " + name + " :: не то исключение: " + ex.GetType().Name); }
}

// ================= State machine =================
static LeaveStatus[][] AllowedMatrix() =>
    [
        [LeaveStatus.Draft, LeaveStatus.Submitted, LeaveStatus.Cancelled],
        [LeaveStatus.Submitted, LeaveStatus.Approved, LeaveStatus.Declined, LeaveStatus.Cancelled],
        [LeaveStatus.Approved, LeaveStatus.Cancelled],
        [LeaveStatus.Declined, LeaveStatus.Submitted],
        [LeaveStatus.Cancelled],
    ];

Console.WriteLine("[State]");
var matrix = AllowedMatrix();
foreach (var cur in Enum.GetValues<LeaveStatus>())
    foreach (var next in Enum.GetValues<LeaveStatus>())
    {
        var state = LeaveState.For(cur);
        var allowed = state.AllowedTransitions.Contains(next);
        var name = cur + " -> " + next;
        if (allowed)
        {
            try { state.EnsureCanTransitionTo(next); Ok(name, true); }
            catch (Exception ex) { Ok(name, false, ex.Message); }
        }
        else Throws<InvalidOperationException>(name, () => state.EnsureCanTransitionTo(next));
    }

Console.WriteLine("[State] фабрика/свойство");
Throws<ArgumentOutOfRangeException>("For(unknown)", () => LeaveState.For((LeaveStatus)99));
Ok("Status property", LeaveState.For(LeaveStatus.Approved).Status == LeaveStatus.Approved);

// ================= Balance =================
var repo = new FakeRepo();
var balance = new BalanceService(repo, new BalanceGate());

Console.WriteLine("[Balance]");
Ok("нет заявок -> 28", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 28);

repo.Items =
[
    L(1, 1, LeaveStatus.Draft, LeaveType.Annual, "2026-06-01", "2026-06-05"),
    L(2, 1, LeaveStatus.Submitted, LeaveType.Annual, "2026-06-01", "2026-06-05"),
    L(3, 1, LeaveStatus.Declined, LeaveType.Annual, "2026-06-01", "2026-06-05"),
    L(4, 1, LeaveStatus.Cancelled, LeaveType.Annual, "2026-06-01", "2026-06-05"),
    L(5, 1, LeaveStatus.Approved, LeaveType.Sick, "2026-06-01", "2026-06-05"),
    L(6, 1, LeaveStatus.Approved, LeaveType.Unpaid, "2026-06-01", "2026-06-05"),
    L(7, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-08-03", "2026-08-10"),
];
Ok("только Approved+Annual (8 дн) -> 20", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 20);

repo.Items = [L(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-06-01", "2026-06-21")];
Ok("дни включительно (21) -> 7", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 7);

repo.Items =
[
    L(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-06-01", "2026-06-07"),
    L(2, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-09-01", "2026-09-07"),
];
Ok("суммирование (14) -> 14", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 14);

repo.Items = [L(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-01-01", "2026-01-30")];
Ok("кламп к 0 (30 дн)", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 0);

repo.Items =
[
    L(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2025-11-01", "2025-11-05"),
    L(2, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-06-01", "2026-06-05"),
];
Ok("заявка вне окна года не учитывается -> 23", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 23);

repo.Items = [L(1, 1, LeaveStatus.Approved, LeaveType.Annual, "2026-12-30", "2027-01-02")];
Ok("стык годов 2026 -> 24", await balance.GetRemainingAnnualDaysAsync(1, 2026) == 24);
Ok("стык годов 2027 -> 24", await balance.GetRemainingAnnualDaysAsync(1, 2027) == 24);

Console.WriteLine("[Balance] окно года, передаваемое репозиторию");
repo.Items = [];
await balance.GetRemainingAnnualDaysAsync(42, 2027);
Ok("from=01.01.2027, to=31.12.2027, userId=42",
    repo.LastRange == (42, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                               new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)));

// ================= BalanceGate =================
Console.WriteLine("[BalanceGate]");
var gate = new BalanceGate();
var first = await gate.LockAsync(1);
var second = gate.LockAsync(1);
await Task.Delay(120);
Ok("тот же пользователь сериализуется", !second.IsCompleted);
first.Dispose();
using var _1 = await second;

using var held = gate.Lock(9);
using var _2 = await gate.LockAsync(10);
Ok("разные пользователи не блокируют друг друга", true);

using var cts = new CancellationTokenSource();
cts.Cancel();

var cancelled = false;
try { await gate.LockAsync(11, cts.Token); }
catch (OperationCanceledException) { cancelled = true; }
Ok("уважает отмену (OperationCanceledException)", cancelled);

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : "ЕСТЬ ПРОВАЛЫ: " + failures);
return failures == 0 ? 0 : 1;

// ---------- helpers ----------
static LeaveRequest L(int id, int userId, LeaveStatus status, LeaveType type, string from, string to)
    => new()
    {
        Id = id,
        UserId = userId,
        Status = status,
        Type = type,
        StartDate = Dt(from),
        EndDate = Dt(to),
        CreatedAt = Dt(from),
    };

static DateTime Dt(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

sealed class FakeRepo : VacationPlanner.Application.Contracts.ILeaveRepository
{
    public List<LeaveRequest> Items { get; set; } = new();
    public (int, DateTime, DateTime)? LastRange { get; private set; }

    public Task<List<LeaveRequest>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Items.ToList());
    public Task<LeaveRequest?> GetByIdAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
    public Task AddAsync(LeaveRequest request, CancellationToken ct = default)
    { Items.Add(request); return Task.CompletedTask; }
    public Task<List<LeaveRequest>> GetByUserInRangeAsync(int userId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        LastRange = (userId, from, to);
        return Task.FromResult(Items.Where(r => r.UserId == userId && r.StartDate <= to && r.EndDate >= from).ToList());
    }
    public Task<List<LeaveRequest>> GetStaleDraftRequestsAsync(DateTime olderThan, CancellationToken ct = default)
        => Task.FromResult(Items.Where(r => r.Status == LeaveStatus.Draft && r.CreatedAt < olderThan).ToList());
    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
}
