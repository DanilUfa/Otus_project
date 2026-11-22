using Microsoft.Extensions.Logging;
using VacationPlanner.Importer.Csv;
using VacationPlanner.Importer.Http;
using VacationPlanner.Importer.Models;
using VacationPlanner.Importer.Persistence;

namespace VacationPlanner.Importer;

internal sealed class ImportRunner(JobStore store, LeaveApi api, ImportSettings settings, ILogger<ImportRunner> logger)
{
    public async Task<ImportJob> RunAsync(string path, int owner, CancellationToken ct)
    {
        await using var file = await CsvSnapshot.OpenAsync(path, settings.MaxFileBytes, settings.MaxRows, ct);
        await using var lease = await store.AcquireAsync(ct);
        int recovered = await JobStore.RecoverAsync(lease, settings.StaleSeconds, ct);
        if (recovered > 0) logger.LogWarning("Recovered {Count} stale jobs", recovered);

        var id = await store.CreateAsync(Path.GetFullPath(path), file.TotalRows, ct);
        logger.LogInformation("Started job {JobId} with {TotalRows} rows", id, file.TotalRows);
        Action<ImportJob> finish;
        try
        {
            await new RowPipeline(store, api, settings).RunAsync(file, owner, id, lease, ct);
            ct.ThrowIfCancellationRequested();
            finish = j => j.Complete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { finish = j => j.Cancel(); }
        catch (Exception ex)
        {
            string reason = ex is ApiFailure ? ex.Message : $"Import interrupted ({ex.GetType().Name}).";
            logger.LogError("Job {JobId} failed: {Reason}. {StackTrace}", id, reason,
                new System.Diagnostics.StackTrace(ex, false));
            finish = j => j.Fail(reason);
        }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var job = await store.ChangeAsync(id, finish, cleanup.Token);
        logger.LogInformation("Finished job {JobId} with {Status}, {Imported} imported, {Errors} errors",
            job.Id, job.Status, job.ImportedRows, job.ErrorRows);
        return job;
    }
}
