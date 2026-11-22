using VacationPlanner.Importer.Enums;
using VacationPlanner.Importer.Models;
using Xunit;

namespace VacationPlanner.Importer.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(" ", 1)]
    [InlineData("csv", 0)]
    [InlineData("csv", -1)]
    public void RequiresPathAndPositiveTotal(string? path, int total) =>
        Assert.ThrowsAny<ArgumentException>(() => new ImportJob(path!, total));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountsAndCompletes(bool error)
    {
        var job = new ImportJob("csv", 1);
        Assert.Throws<InvalidOperationException>(job.Complete);
        job.UpdateHeartbeat();
        Assert.NotNull(job.HeartbeatAt);
        if (error) job.IncrementErrorRows(); else job.IncrementImportedRows();
        Assert.Throws<InvalidOperationException>(job.IncrementImportedRows);
        Assert.Throws<InvalidOperationException>(job.IncrementErrorRows);
        job.Complete();
        Assert.Equal(error ? ImportStatus.DoneWithErrors : ImportStatus.DoneSuccessfully, job.Status);
        CheckTerminal(job);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanStopIncomplete(bool failed)
    {
        var job = new ImportJob("csv", 1);
        if (failed) job.Fail("offline"); else job.Cancel();
        Assert.Equal(failed ? ImportStatus.Failed : ImportStatus.Canceled, job.Status);
        Assert.Equal(failed ? "offline" : null, job.FailureReason);
        CheckTerminal(job);
    }

    private static void CheckTerminal(ImportJob job)
    {
        Assert.NotNull(job.FinishedAt);
        foreach (Action operation in new Action[] { job.Cancel, job.Complete, job.UpdateHeartbeat,
            job.IncrementImportedRows, job.IncrementErrorRows, () => job.Fail("again") })
            Assert.Throws<InvalidOperationException>(operation);
    }
}
