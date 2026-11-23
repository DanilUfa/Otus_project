using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Metrics;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.UnitTests.Fakes;
using Xunit;

namespace VacationPlanner.UnitTests;

public class BalanceCacheTests
{
    private static readonly int Year = DateTime.UtcNow.Year + 2;
    private static DateTime Date(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    private static CreateLeaveRequestRequest Annual(DateTime start, DateTime end)
        => new(2, LeaveType.Annual, start, end, "cache test");

    [Fact]
    public async Task Cache_hit_avoids_query_and_cross_year_invalidation_preserves_other_users()
    {
        var h = new Harness();
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        Assert.Equal(1, h.Repo.InRangeCalls);
        Assert.Equal(TimeSpan.FromSeconds(60), h.Cache.LastTtl);
        await h.Balance.GetRemainingAnnualDaysAsync(2, Year + 1);
        await h.Balance.GetRemainingAnnualDaysAsync(3, Year);
        await h.Leaves.SubmitAsync(Annual(Date(Year, 12, 30), Date(Year + 1, 1, 2)));

        var queries = h.Repo.InRangeCalls;
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year + 1));
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(3, Year));
        Assert.Equal(queries + 2, h.Repo.InRangeCalls);
    }

    [Fact]
    public async Task Draft_submission_approval_cancellation_and_decline_invalidate_after_save()
    {
        var h = new Harness();
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        var draft = await h.Leaves.SaveDraftAsync(Annual(Date(Year, 3, 1), Date(Year, 3, 2)));
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        await h.Leaves.SubmitDraftAsync(draft, 2);
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        var queries = h.Repo.InRangeCalls;
        await h.Leaves.UpdateStatusAsync(draft, LeaveStatus.Approved, 1);
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        Assert.Equal(queries + 1, h.Repo.InRangeCalls);
        await h.Leaves.CancelAsync(draft, 2);
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));

        var submitted = await h.Leaves.SubmitAsync(Annual(Date(Year, 4, 1), Date(Year, 4, 2)));
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        await h.Leaves.UpdateStatusAsync(submitted.Id, LeaveStatus.Declined, 1);
        Assert.Equal(28, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
    }

    [Fact]
    public async Task In_flight_cache_fill_cannot_restore_stale_value_after_mutation()
    {
        var h = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Cache.BeforeSet = async () =>
        {
            h.Cache.BeforeSet = null;
            entered.TrySetResult();
            await release.Task;
        };
        var oldRead = h.Balance.GetRemainingAnnualDaysAsync(2, Year);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<LeaveRequestDto>? mutation = null;
        try
        {
            mutation = h.Leaves.SubmitAsync(Annual(Date(Year, 6, 1), Date(Year, 6, 2)));
            Assert.False(mutation.IsCompleted);
            Assert.Empty(h.Repo.Items);
        }
        finally { release.TrySetResult(); }

        Assert.Equal(28, await oldRead.WaitAsync(TimeSpan.FromSeconds(5)));
        await mutation!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
        Assert.Equal(26, await h.Balance.GetRemainingAnnualDaysAsync(2, Year));
    }

    private sealed class Harness
    {
        public FakeLeaveRepository Repo { get; } = new();
        public TestCache Cache { get; } = new();
        public BalanceService Balance { get; }
        public LeaveService Leaves { get; }

        public Harness()
        {
            var gate = new BalanceGate();
            var users = new FakeUserRepository();
            var manager = new User { Id = 1, Name = "Manager", Email = "manager@test.local" };
            users.Users.Add(manager);
            users.Users.Add(new User { Id = 2, Name = "Employee", Email = "employee@test.local", ManagerId = 1, Manager = manager });
            Balance = new BalanceService(Repo, gate, Cache);
            Leaves = new LeaveService(Repo, users, new FakeOutboxRepository(), new FakeUnitOfWork(),
                new VacationMetrics(), new NoRealtime(), Balance, gate);
        }
    }

    private sealed class NoRealtime : IRealtimeNotifier
    {
        public Task NotifyLeaveCreatedAsync(LeaveRequestDto dto, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyStatusChangedAsync(int requestId, string status, int ownerUserId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> values = new();
        public Func<Task>? BeforeSet { get; set; }
        public TimeSpan? LastTtl { get; private set; }
        public byte[]? Get(string key) => values.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Get(key));
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            LastTtl = options.AbsoluteExpirationRelativeToNow;
            values[key] = value;
        }
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (BeforeSet is { } block) await block();
            token.ThrowIfCancellationRequested();
            Set(key, value, options);
        }
        public void Remove(string key) => values.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Remove(key);
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
