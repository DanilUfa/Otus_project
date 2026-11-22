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
            await foreach (var record in file.RowsAsync(ct))
            {
                var (row, issue) = RowValidator.Parse(record, owner);
                Guid? reserved = null;
                var result = new ApiResult(null, issue);
                if (row != null)
                {
                    if (await store.ReserveAsync(id, record.Line, row.RequestId, ct))
                    {
                        reserved = row.RequestId;
                        result = await api.SubmitAsync(row.Payload, ct);
                    }
                    else result = new(null, new("Duplicate", "RequestId already reserved; inspect the original dispatch."));
                }
                using var save = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await store.RecordAsync(id, record.Line, reserved, result, save.Token);
            }
            ct.ThrowIfCancellationRequested();
            finish = j => j.Complete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { finish = j => j.Cancel(); }
        catch (Exception ex)
        {
            string reason = ex is ApiFailure ? ex.Message : $"Import interrupted ({ex.GetType().Name}).";
            logger.LogError("Job {JobId} failed: {Reason}", id, reason);
            finish = j => j.Fail(reason);
        }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await store.ChangeAsync(id, finish, cleanup.Token);
    }
}
