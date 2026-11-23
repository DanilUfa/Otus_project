using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;
using VacationPlanner.Infrastructure.Notifications;
using VacationPlanner.Infrastructure.Repositories;
using Xunit;

namespace VacationPlanner.UnitTests;

public class DeliveryIntegrationTests
{
    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IMPORTER_TEST_POSTGRES")))
                Skip = "Set IMPORTER_TEST_POSTGRES to a disposable PostgreSQL server with CREATEDB permission.";
        }
    }

    [PostgresFact]
    public async Task Refresh_rotation_is_atomic_with_two_database_connections()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.App();
        await seed.Database.MigrateAsync();
        Assert.False(seed.Database.HasPendingModelChanges());
        var user = new User { Name = "Test", Email = "rotation@test.local" };
        seed.Users.Add(user);
        await seed.SaveChangesAsync();
        seed.RefreshTokens.Add(Token("original", user.Id));
        await seed.SaveChangesAsync();

        await using var first = database.App();
        await using var second = database.App();
        var a = new RefreshTokenRepository(first);
        var b = new RefreshTokenRepository(second);
        var oldA = (await a.GetByHashAsync("original"))!;
        var oldB = (await b.GetByHashAsync("original"))!;
        var results = await Task.WhenAll(a.TryRotateAsync(oldA, Token("replacement-a", user.Id)),
            b.TryRotateAsync(oldB, Token("replacement-b", user.Id)));
        Assert.Single(results, x => x);
        seed.ChangeTracker.Clear();
        Assert.Equal(2, await seed.RefreshTokens.CountAsync());
        Assert.Single(await seed.RefreshTokens.Where(t => t.RevokedAt == null).ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrent_out_of_order_projection_keeps_latest_tombstone()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.Reporting();
        await seed.Database.MigrateAsync();
        Assert.False(seed.Database.HasPendingModelChanges());
        var start = new DateTime(2028, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var submitted = new LeaveRequestSubmitted(42, 2, "Employee", LeaveType.Annual, start,
            start.AddDays(1), start, "e@test.local", "m@test.local", 1);
        var cancelled = new LeaveRequestStatusChanged(42, 2, LeaveStatus.Cancelled, start,
            start.AddDays(1), "e@test.local", 3, "Employee", LeaveType.Annual);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            await using var db = database.Reporting();
            if (i % 2 == 0) await CalendarProjection.ApplyAsync(db, submitted, default);
            else await CalendarProjection.ApplyAsync(db, cancelled, default);
        }));
        var row = Assert.Single(await seed.ReadLeaves.ToListAsync());
        Assert.Equal(LeaveStatus.Cancelled, row.Status);
        Assert.Equal(3, row.Revision);
        Assert.Equal("Employee", row.UserName);
    }

    [PostgresFact]
    public async Task Outbox_recovers_legacy_failed_rows_and_respects_backoff()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.App();
        await db.Database.MigrateAsync();
        var legacy = new OutboxMessage { Id = Guid.NewGuid(), Type = "test", Failed = true, FailedCount = 5 };
        db.OutboxMessages.AddRange(legacy,
            new OutboxMessage { Id = Guid.NewGuid(), NextAttemptAt = DateTime.UtcNow.AddHours(1) },
            new OutboxMessage { Id = Guid.NewGuid(), PublishedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var repository = new OutboxRepository(db);
        Assert.Equal(legacy.Id, Assert.Single(await repository.GetPendingAsync(50)).Id);
        legacy.ScheduleRetry(DateTime.UtcNow);
        await repository.SaveAsync();
        Assert.Empty(await repository.GetPendingAsync(50));
        legacy.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        await repository.SaveAsync();
        Assert.Equal(legacy.Id, Assert.Single(await repository.GetPendingAsync(50)).Id);
    }

    [Fact]
    public async Task Smtp_failure_is_propagated_for_message_retry()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Email:Host"] = "127.0.0.1",
            ["Notifications:Email:Port"] = "1"
        }).Build();
        var sender = new EmailNotificationSender(config, NullLogger<EmailNotificationSender>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendAsync("recipient@test.local", "test", "test", timeout.Token));
    }

    private static RefreshToken Token(string hash, int userId) => new()
    {
        UserId = userId, TokenHash = hash, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
    };

    private sealed class TestDatabase(string admin, string name, string connection) : IAsyncDisposable
    {
        public AppDbContext App() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        public ReportingDbContext Reporting() => new(new DbContextOptionsBuilder<ReportingDbContext>().UseNpgsql(connection).Options);

        public static async Task<TestDatabase> CreateAsync()
        {
            var admin = Environment.GetEnvironmentVariable("IMPORTER_TEST_POSTGRES")!;
            var name = "delivery_test_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
            return new(admin, name, new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
