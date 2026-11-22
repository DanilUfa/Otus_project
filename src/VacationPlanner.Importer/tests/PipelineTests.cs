using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VacationPlanner.Importer.Enums;
using VacationPlanner.Importer.Models;
using VacationPlanner.Importer.Persistence;
using Xunit;

namespace VacationPlanner.Importer.Tests;

public sealed partial class DatabaseTests
{
    private static TestCsv ManyRows(int count)
    {
        var start = new DateOnly(2031, 1, 1);
        return new TestCsv(string.Join('\n', Enumerable.Range(0, count).Select(i =>
            $"{Guid.NewGuid()},2,Sick,{start.AddDays(i):yyyy-MM-dd},{start.AddDays(i):yyyy-MM-dd},")));
    }

    [PostgreSqlFact]
    public async Task BoundedWorkersPersistEveryOutcomeOnce()
    {
        using var csv = ManyRows(160);
        int current = 0, calls = 0;
        var saturated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, ct) =>
        {
            var active = Interlocked.Increment(ref current);
            try
            {
                Assert.InRange(active, 1, 4);
                if (active == 4) saturated.TrySetResult();
                await saturated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                await Task.Delay(5, ct);
                return Interlocked.Increment(ref calls) % 4 == 0
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "validation" }) }
                    : Receipt();
            }
            finally { Interlocked.Decrement(ref current); }
        });
        var job = await Runner(handler, new() { Workers = 4, Capacity = 2 }).RunAsync(csv.Path, 2, default);
        Assert.Equal(ImportStatus.DoneWithErrors, job.Status);
        Assert.Equal(160, calls);
        Assert.Equal(120, job.ImportedRows);
        Assert.Equal(40, job.ErrorRows);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(40, await db.ImportErrors.Select(x => x.RowNumber).Distinct().CountAsync());
        Assert.Equal(120, await db.Dispatches.CountAsync(x => x.LeaveId != null));
        Assert.Equal(40, await db.Dispatches.CountAsync(x => x.Rejected));
    }

    [PostgreSqlFact]
    public async Task HeartbeatCancellationAndRestart()
    {
        using var csv = ManyRows(12);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        using var handler = new Handler(async (_, ct) =>
        {
            Interlocked.Increment(ref active);
            try
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Receipt();
            }
            finally { Interlocked.Decrement(ref active); }
        });
        var settings = new ImportSettings { Workers = 1, Capacity = 1, HeartbeatSeconds = 1 };
        var running = Runner(handler, settings).RunAsync(csv.Path, 2, stop.Token);
        await entered.Task.WaitAsync(stop.Token);
        await using var db = await Factory.CreateDbContextAsync();
        while (!await db.ImportJobs.AnyAsync(j => j.HeartbeatAt != null, stop.Token))
            await Task.Delay(25, stop.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new JobStore(Factory).AcquireAsync(stop.Token));
        await stop.CancelAsync();
        var canceled = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, active);
        Assert.Equal(ImportStatus.Canceled, canceled.Status);
        Assert.Equal(0, canceled.ErrorRows);
        Assert.Equal(0, canceled.ImportedRows);
        Assert.Null(canceled.FailureReason);
        Assert.Null((await db.Dispatches.SingleAsync()).LeaveId);

        using var healthy = new Handler((_, _) => Task.FromResult(Receipt()));
        var replay = await Runner(healthy, settings).RunAsync(csv.Path, 2, default);
        Assert.Equal(11, replay.ImportedRows);
        Assert.Equal(1, replay.ErrorRows);
    }

    [PostgreSqlFact]
    public async Task LostResponseStopsWorkersAndRecoveryOnlyFailsStaleJobs()
    {
        using var csv = ManyRows(8);
        using var offline = new Handler((_, _) => throw new HttpRequestException("unreachable"));
        var job = await Runner(offline, new() { Workers = 1, Capacity = 1 }).RunAsync(csv.Path, 2, default)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(ImportStatus.Failed, job.Status);
        Assert.Contains("confirm POST", job.FailureReason);
        Assert.Equal(0, job.ImportedRows + job.ErrorRows);

        await using var db = await Factory.CreateDbContextAsync();
        var abandoned = new ImportJob("stopped", 2);
        var fresh = new ImportJob("fresh", 1);
        db.AddRange(abandoned, fresh);
        await db.SaveChangesAsync();
        await db.ImportJobs.Where(j => j.Id == abandoned.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.StartedAt, DateTime.UtcNow.AddMinutes(-10)));
        await using var lease = await new JobStore(Factory).AcquireAsync(default);
        Assert.Equal(1, await JobStore.RecoverAsync(lease, 60, default));
        Assert.Equal(0, await JobStore.RecoverAsync(lease, 60, default));
        await db.Entry(abandoned).ReloadAsync();
        Assert.Equal(ImportStatus.Failed, abandoned.Status);
        Assert.NotNull(abandoned.FinishedAt);
        await db.Entry(fresh).ReloadAsync();
        Assert.Equal(ImportStatus.InProcess, fresh.Status);
    }

    [PostgreSqlFact]
    public async Task ConflictingRowsPreserveCsvOrder()
    {
        var key = Guid.NewGuid();
        using var csv = new TestCsv($"{key},2,Annual,2030-02-01,2030-02-02,first\n" +
            $"{Guid.NewGuid()},2,Annual,2030-12-30,2031-01-04,second\n" +
            $"{Guid.NewGuid()},2,Sick,2031-01-03,2031-01-05,third\n" +
            $"{key},2,Unpaid,2033-01-03,2033-01-05,fourth");
        var observed = new List<string>();
        using var handler = new Handler(async (request, ct) =>
        {
            var body = await request.Content!.ReadFromJsonAsync<Csv.LeavePayload>(ct);
            lock (observed) observed.Add(body!.Reason);
            await Task.Delay(35, ct);
            return Receipt();
        });
        var result = await Runner(handler, new()).RunAsync(csv.Path, 2, default);
        Assert.Equal(new[] { "first", "second", "third" }, observed);
        Assert.Equal(3, result.ImportedRows);
        Assert.Equal(1, result.ErrorRows);
    }

    [PostgreSqlFact]
    public async Task InvalidPreflightLeavesNoJob()
    {
        using var csv = new TestCsv("");
        using var handler = new Handler((_, _) => throw new InvalidOperationException());
        await Assert.ThrowsAsync<InvalidDataException>(() => Runner(handler, new()).RunAsync(csv.Path, 2, default));
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Empty(await db.ImportJobs.ToListAsync());
    }
}
