using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using VacationPlanner.Importer.Csv;
using VacationPlanner.Importer.Enums;
using VacationPlanner.Importer.Http;
using VacationPlanner.Importer.Persistence;

namespace VacationPlanner.Importer;

internal sealed class RowPipeline(JobStore store, LeaveApi api, ImportSettings settings)
{
    private sealed record Work(int Line, LeaveRow? Row, RowIssue? Issue)
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task RunAsync(CsvSnapshot file, int owner, int job, ImporterDbContext lease, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var writer = new SemaphoreSlim(1);
        var channel = Channel.CreateBounded<Work>(new BoundedChannelOptions(settings.Capacity) { SingleWriter = true });

        // Any failed stage wakes blocked producers, readers, and in-flight HTTP calls.
        async Task Supervise(Func<Task> stage)
        {
            try { await stage(); }
            catch { await stop.CancelAsync(); throw; }
        }

        var heartbeat = Supervise(async () =>
        {
            try { await HeartbeatAsync(lease, job, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        var workers = Enumerable.Range(0, settings.Workers).Select(_ => Supervise(async () =>
        {
            await foreach (var work in channel.Reader.ReadAllAsync(stop.Token))
            {
                stop.Token.ThrowIfCancellationRequested();
                await ProcessAsync(job, work, writer, stop.Token);
                work.Done.SetResult();
            }
        })).ToArray();

        var producer = Supervise(async () =>
        {
            try { await ProduceAsync(channel.Writer, file, owner, stop.Token); }
            finally { channel.Writer.TryComplete(); }
        });

        async Task FinishRowsAsync()
        {
            try { await Task.WhenAll(workers.Append(producer)); }
            finally { await stop.CancelAsync(); }
        }
        await Task.WhenAll(FinishRowsAsync(), heartbeat);
        ct.ThrowIfCancellationRequested();
    }

    private static async Task ProduceAsync(ChannelWriter<Work> channel, CsvSnapshot file, int owner, CancellationToken ct)
    {
        var pending = new List<Work>();
        await foreach (var record in file.RowsAsync(ct))
        {
            var (row, issue) = RowValidator.Parse(record, owner);
            var work = new Work(record.Line, row, issue);
            pending.RemoveAll(previous => previous.Done.Task.IsCompleted);
            if (row != null)
            {
                foreach (var previous in pending)
                    if (previous.Row is { } other && Conflicts(row, other))
                        await previous.Done.Task.WaitAsync(ct);
                pending.Add(work);
            }
            await channel.WriteAsync(work, ct);
        }
    }

    // The API checks conflicts and annual balance before opening its INSERT transaction.
    // Preserve CSV order where two submissions from this importer can affect those checks.
    private static bool Conflicts(LeaveRow a, LeaveRow b)
    {
        var x = a.Payload;
        var y = b.Payload;
        return a.RequestId == b.RequestId ||
            (x.StartDate <= y.EndDate && y.StartDate <= x.EndDate) ||
            (x.Type == LeaveType.Annual && y.Type == LeaveType.Annual &&
            x.StartDate.Year <= y.EndDate.Year && y.StartDate.Year <= x.EndDate.Year);
    }

    private async Task ProcessAsync(int job, Work work, SemaphoreSlim writer, CancellationToken ct)
    {
        Guid? reserved = null;
        var result = new ApiResult(null, work.Issue);
        if (work.Row is { } row)
        {
            var duplicate = await store.ReserveAsync(job, work.Line, row.RequestId, ct);
            if (duplicate == null)
            {
                reserved = row.RequestId;
                result = await api.SubmitAsync(row.Payload, ct);
            }
            else result = new(null, duplicate);
        }

        // Finish recording a known outcome even if a sibling or Ctrl+C has stopped intake.
        using var save = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await writer.WaitAsync(save.Token);
        try { await store.RecordAsync(job, work.Line, reserved, result, save.Token); }
        finally { writer.Release(); }
    }

    private async Task HeartbeatAsync(ImporterDbContext lease, int job, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.HeartbeatSeconds));
        while (await timer.WaitForNextTickAsync(ct))
        {
            int updated = await lease.ImportJobs.Where(j => j.Id == job && j.Status == ImportStatus.InProcess)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.HeartbeatAt, DateTime.UtcNow), ct);
            if (updated != 1) throw new InvalidOperationException("Lost ownership of the active import.");
        }
    }
}
