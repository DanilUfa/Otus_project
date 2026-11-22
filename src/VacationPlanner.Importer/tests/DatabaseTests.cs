using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VacationPlanner.Importer.Enums;
using VacationPlanner.Importer.Http;
using VacationPlanner.Importer.Persistence;
using Xunit;

namespace VacationPlanner.Importer.Tests;

public sealed class PostgreSqlFact : FactAttribute
{
    public PostgreSqlFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IMPORTER_TEST_POSTGRES")))
            Skip = "Set IMPORTER_TEST_POSTGRES to a PostgreSQL connection with CREATE DATABASE permission.";
    }
}

public sealed partial class DatabaseTests : IAsyncLifetime
{
    private readonly string dbName = "importer_test_" + Guid.NewGuid().ToString("N");
    private readonly string? admin = Environment.GetEnvironmentVariable("IMPORTER_TEST_POSTGRES");
    private ServiceProvider? services;
    private IDbContextFactory<ImporterDbContext> Factory => services!.GetRequiredService<IDbContextFactory<ImporterDbContext>>();

    public async Task InitializeAsync()
    {
        if (admin == null) return;
        await AdminSql($"CREATE DATABASE {dbName}");
        var connection = new NpgsqlConnectionStringBuilder(admin) { Database = dbName, Pooling = false };
        services = new ServiceCollection().AddDbContextFactory<ImporterDbContext>(o => o.UseNpgsql(connection.ToString()))
            .BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    public async Task DisposeAsync()
    {
        if (services == null) return;
        await services.DisposeAsync();
        await AdminSql($"DROP DATABASE {dbName}");
    }

    private async Task AdminSql(string sql)
    {
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private ImportRunner Runner(HttpMessageHandler handler, ImportSettings settings) =>
        new(new JobStore(Factory), new LeaveApi(new HttpClient(handler) { BaseAddress = new Uri("http://test/") }),
            settings, NullLogger<ImportRunner>.Instance);

    [PostgreSqlFact]
    public async Task SequentialMixedAndReplay()
    {
        var key = Guid.NewGuid();
        using var csv = new TestCsv($"{key},2,Sick,2030-02-01,2030-02-02,one\n{Guid.NewGuid()},2,Unknown,2030-02-01,2030-02-02,two");
        int sent = 0;
        using var handler = new Handler((_, _) => {
            Interlocked.Increment(ref sent);
            return Task.FromResult(Receipt());
        });
        var runner = Runner(handler, new() { Workers = 1 });
        var result = await runner.RunAsync(csv.Path, 2, default);
        Assert.Equal(ImportStatus.DoneWithErrors, result.Status);
        Assert.Equal(result.TotalRows, result.ImportedRows + result.ErrorRows);
        Assert.Equal(1, result.ImportedRows);
        await using var db = await Factory.CreateDbContextAsync();
        var saved = await db.ImportJobs.SingleAsync();
        Assert.Equal(result.StartedAt, saved.StartedAt);
        Assert.Equal(result.FinishedAt!.Value, saved.FinishedAt!.Value, TimeSpan.FromMicroseconds(1));
        Assert.Equal(3, (await db.ImportErrors.SingleAsync()).RowNumber);
        Assert.Equal(7, (await db.Dispatches.SingleAsync()).LeaveId);
        var replay = await runner.RunAsync(csv.Path, 2, default);
        Assert.Equal(2, replay.ErrorRows);
        Assert.Equal(1, sent);
    }

    internal static HttpResponseMessage Receipt() => new(HttpStatusCode.Created)
        { Content = JsonContent.Create(new { id = 7, userId = 2 }) };
}

internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
}

internal sealed class TestCsv : IDisposable
{
    public const string Header = "RequestId,UserId,Type,StartDate,EndDate,Reason";
    public string Path { get; } = System.IO.Path.GetTempFileName();
    public TestCsv(string rows) => File.WriteAllText(Path, Header + "\n" + rows);
    public void Dispose() => File.Delete(Path);
}
