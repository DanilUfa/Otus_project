using Microsoft.EntityFrameworkCore;
using VacationPlanner.Importer.Csv;
using VacationPlanner.Importer.Enums;
using VacationPlanner.Importer.Http;
using VacationPlanner.Importer.Models;

namespace VacationPlanner.Importer.Persistence;

internal sealed class JobStore(IDbContextFactory<ImporterDbContext> contexts)
{
    // A single session owns imports and crash recovery. PostgreSQL releases the lock on disconnect.
    public async Task<ImporterDbContext> AcquireAsync(CancellationToken ct)
    {
        var db = await contexts.CreateDbContextAsync(ct);
        try
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false };
            db.Database.SetConnectionString(connection.ConnectionString);
            await db.Database.OpenConnectionAsync(ct);
            var acquired = await db.Database.SqlQuery<bool>(
                $"SELECT pg_try_advisory_lock(6823920317) AS \"Value\"").SingleAsync(ct);
            if (!acquired) throw new InvalidOperationException("Another importer owns this database.");
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public static Task<int> RecoverAsync(ImporterDbContext owner, int seconds, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-seconds);
        return owner.ImportJobs.Where(j => j.Status == ImportStatus.InProcess && (j.HeartbeatAt ?? j.StartedAt) < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ImportStatus.Failed)
                .SetProperty(j => j.FinishedAt, DateTime.UtcNow)
                .SetProperty(j => j.FailureReason, "Importer stopped updating heartbeat; verify pending dispatches."), ct);
    }

    public async Task<int> CreateAsync(string path, int total, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var job = new ImportJob(path, total);
        db.Add(job);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }

    public async Task<RowIssue?> ReserveAsync(int job, int line, Guid request, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "Dispatches" ("RequestId", "ImportJobId", "RowNumber", "Rejected") VALUES ({request}, {job}, {line}, false) ON CONFLICT ("RequestId") DO NOTHING""", ct);
        if (inserted == 1) return null;
        var original = await db.Dispatches.AsNoTracking().SingleAsync(d => d.RequestId == request, ct);
        return new("Duplicate", $"RequestId already reserved by job {original.ImportJobId}, line {original.RowNumber}.");
    }

    // Workers serialize row results. The heartbeat updates only HeartbeatAt on its own connection.
    public async Task RecordAsync(int jobId, int line, Guid? reserved, ApiResult result, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var job = await db.ImportJobs.SingleAsync(j => j.Id == jobId, ct);
        if (reserved is Guid request)
        {
            var dispatch = await db.Dispatches.SingleAsync(d => d.RequestId == request, ct);
            dispatch.LeaveId = result.LeaveId;
            dispatch.Rejected = result.Error != null;
        }
        if (result.Error is RowIssue error)
        {
            job.IncrementErrorRows();
            db.Add(new ImportError { ImportJobId = jobId, RowNumber = line, ErrorCode = error.Code, Message = error.Message });
        }
        else job.IncrementImportedRows();
        await db.SaveChangesAsync(ct);
    }

    public async Task<ImportJob> ChangeAsync(int id, Action<ImportJob> change, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var job = await db.ImportJobs.SingleAsync(j => j.Id == id, ct);
        change(job);
        await db.SaveChangesAsync(ct);
        return job;
    }
}
