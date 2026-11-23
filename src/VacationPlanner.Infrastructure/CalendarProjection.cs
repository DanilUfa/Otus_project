using Microsoft.EntityFrameworkCore;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;

namespace VacationPlanner.Infrastructure;

public static class CalendarProjection
{
    public static Task ApplyAsync(ReportingDbContext db, LeaveRequestSubmitted message, CancellationToken ct)
        => ApplyAsync(db, message.RequestId, row => row.Apply(message), ct);

    public static Task ApplyAsync(ReportingDbContext db, LeaveRequestStatusChanged message, CancellationToken ct)
        => ApplyAsync(db, message.RequestId, row => row.Apply(message), ct);

    private static async Task ApplyAsync(ReportingDbContext db, int requestId, Func<ReadLeave, bool> apply, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Separate RabbitMQ queues may process the same request concurrently, including its first event.
        // A transaction-scoped PostgreSQL lock serializes both inserts and updates across service instances.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(753482, {requestId})", ct);
        var row = await db.ReadLeaves.SingleOrDefaultAsync(r => r.RequestId == requestId, ct);
        var added = row is null;
        row ??= new ReadLeave();
        if (apply(row))
        {
            if (added) db.ReadLeaves.Add(row);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
