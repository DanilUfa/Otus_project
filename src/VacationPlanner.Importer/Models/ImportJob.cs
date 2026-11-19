using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using VacationPlanner.Importer.Enums;

namespace VacationPlanner.Importer.Models;

internal sealed class ImportJob
{
    [SetsRequiredMembers]
    public ImportJob(string filePath, int totalRows)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (totalRows <= 0)
            throw new ArgumentException($"{nameof(totalRows)} can't be lower or equals to 0");

        FilePath = filePath;
        TotalRows = totalRows;
    }

    public int Id { get; init; }
    public required string FilePath { get; init; }
    public ImportStatus Status { get; private set; } = ImportStatus.InProcess;

    public int TotalRows { get; private set; }
    public int ImportedRows { get; private set; }
    public int ErrorRows { get; private set; }

    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public DateTime? HeartbeatAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public void Complete()
    {
        EnsureInProcess();

        if (TotalRows != ImportedRows + ErrorRows)
            throw new InvalidOperationException("Can't close incomplete job");

        FinishedAt = DateTime.UtcNow;
        Status = ErrorRows == 0 ? ImportStatus.DoneSuccessfully : ImportStatus.DoneWithErrors;
    }

    public void IncrementImportedRows()
    {
        EnsureInProcess();
        EnsureCanIncrement();

        ImportedRows++;
    }

    public void IncrementErrorRows()
    {
        EnsureInProcess();
        EnsureCanIncrement();

        ErrorRows++;
    }

    public void UpdateHeartbeat()
    {
        EnsureInProcess();
        HeartbeatAt = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        EnsureInProcess();
        ArgumentNullException.ThrowIfNull(reason);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException($"{nameof(reason)} can't be whitespace");

        FailureReason = reason;
        FinishedAt = DateTime.UtcNow;
        Status = ImportStatus.Failed;
    }

    public void Cancel()
    {
        EnsureInProcess();

        FinishedAt = DateTime.UtcNow;
        Status = ImportStatus.Canceled;
    }

    private void EnsureCanIncrement()
    {
        if (TotalRows <= ImportedRows + ErrorRows)
            throw new InvalidOperationException("Can't increment: TotalRows overflow");
    }

    private void EnsureInProcess()
    {
        if (Status == ImportStatus.InProcess)
            return;
        var message = $"Invalid operation for current status: {Enum.GetName(Status)}";
        throw new InvalidOperationException(message);
    }
}
