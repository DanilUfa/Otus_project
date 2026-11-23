using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VacationPlanner.Importer;
using VacationPlanner.Importer.Enums;
using VacationPlanner.Importer.Http;
using VacationPlanner.Importer.Models;
using VacationPlanner.Importer.Persistence;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var termination = OperatingSystem.IsWindows() ? null :
    System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,
        signal => { signal.Cancel = true; stop.Cancel(); });
var ct = stop.Token;
if (args is ["--help"] or ["-h"]) return Usage(0);
bool jobCommand = args is ["status" or "errors" or "dispatches", "--job", _];
int jobId = 0;
if (!(args is ["migrate" or "list" or "recover"] or ["import", "--file", _] ||
    (jobCommand && int.TryParse(args[2], out jobId) && jobId > 0))) return Usage(2);

try
{
    var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
    var settings = config.GetSection("Importer").Get<ImportSettings>() ?? new();
    settings.Validate();
    var connection = config.GetConnectionString("Importer");
    if (string.IsNullOrWhiteSpace(connection))
    {
        Console.Error.WriteLine("Set ConnectionStrings__Importer for the dedicated importer database.");
        return 2;
    }
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning)
        .AddFilter("VacationPlanner.Importer", LogLevel.Information)
        .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None));
    services.AddSingleton(settings);
    services.AddDbContextFactory<ImporterDbContext>(o => o.UseNpgsql(connection));
    services.AddSingleton<JobStore>();
    services.AddTransient<ImportRunner>();
    services.AddHttpClient<LeaveApi>(http =>
    {
        http.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        http.MaxResponseContentBufferSize = 65536;
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    await using var provider = services.BuildServiceProvider();
    var factory = provider.GetRequiredService<IDbContextFactory<ImporterDbContext>>();
    await using var db = await factory.CreateDbContextAsync(ct);
    switch (args[0])
    {
        case "migrate":
            await db.Database.MigrateAsync(ct);
            Console.WriteLine("Importer migrations applied.");
            return 0;
        case "recover":
            await using (var lease = await provider.GetRequiredService<JobStore>().AcquireAsync(ct))
                Console.WriteLine($"Recovered jobs: {await JobStore.RecoverAsync(lease, settings.StaleSeconds, ct)}");
            return 0;
        case "import":
            int owner = LeaveApi.ReadOwner(settings.Token);
            var runner = provider.GetRequiredService<ImportRunner>();
            var result = await runner.RunAsync(args[2], owner, ct);
            Print(result);
            if (!ct.IsCancellationRequested) await PrintErrors(db, result.Id, ct);
            return result.Status switch { ImportStatus.DoneSuccessfully => 0, ImportStatus.DoneWithErrors => 3,
                ImportStatus.Canceled => 130, _ => 4 };
        case "list":
            await foreach (var job in db.ImportJobs.AsNoTracking().OrderByDescending(j => j.Id).AsAsyncEnumerable().WithCancellation(ct))
                Print(job);
            return 0;
        default:
            var saved = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, ct);
            if (saved == null) { Console.Error.WriteLine("Job not found."); return 2; }
            if (args[0] == "status") Print(saved);
            else if (args[0] == "errors") await PrintErrors(db, jobId, ct);
            else
            {
                var rows = db.Dispatches.AsNoTracking().Where(d => d.ImportJobId == jobId).OrderBy(d => d.RowNumber);
                await foreach (var d in rows.AsAsyncEnumerable().WithCancellation(ct))
                    Console.WriteLine($"Line={d.RowNumber} RequestId={d.RequestId} Result={d.LeaveId?.ToString() ?? (d.Rejected ? "Rejected" : "Unconfirmed")}");
            }
            return 0;
    }
}
catch (OperationCanceledException) when (ct.IsCancellationRequested) { return 130; }
catch (InvalidDataException ex) { Console.Error.WriteLine(ex.Message); return 2; }
catch (DecoderFallbackException) { Console.Error.WriteLine("CSV is not valid UTF-8."); return 2; }
catch (FileNotFoundException ex) when (args is ["import", "--file", _] && ex.FileName == Path.GetFullPath(args[2]))
{
    Console.Error.WriteLine($"CSV file not found: {args[2]}");
    return 2;
}
catch (DirectoryNotFoundException) when (args is ["import", "--file", _])
{
    Console.Error.WriteLine($"Directory not found for CSV: {args[2]}");
    return 2;
}
catch (UnauthorizedAccessException)
{
    Console.Error.WriteLine("Access denied while reading or writing a file.");
    return 2;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("Invalid importer configuration or argument.");
    return 2;
}
catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
{
    Console.Error.WriteLine("Importer table not found. Run the migrate command.");
    return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Command '{args[0]}' failed ({ex.GetType().Name}).");
    return 4;
}

static void Print(ImportJob j) => Console.WriteLine(
    $"JobId={j.Id} Status={j.Status} Total={j.TotalRows} Imported={j.ImportedRows} Errors={j.ErrorRows} " +
    $"Started={j.StartedAt:O} Heartbeat={j.HeartbeatAt:O} Finished={j.FinishedAt:O} {j.FailureReason}");

static async Task PrintErrors(ImporterDbContext db, int id, CancellationToken ct)
{
    var errors = db.ImportErrors.AsNoTracking().Where(e => e.ImportJobId == id).OrderBy(e => e.RowNumber);
    await foreach (var e in errors.AsAsyncEnumerable().WithCancellation(ct))
        Console.WriteLine($"Line={e.RowNumber} {e.ErrorCode}: {e.Message}");
}

static int Usage(int code)
{
    Console.WriteLine("importer migrate | import --file <csv> | list | status|errors|dispatches --job <id> | recover");
    return code;
}
