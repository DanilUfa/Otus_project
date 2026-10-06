using System.Text;
using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VacationPlanner.Domain;
using VacationPlanner.Infrastructure;
using VacationPlanner.Reporting.Consumers;

var builder = WebApplication.CreateBuilder(args);
// Логирование в Console (как в контейнере): EventLog на Windows без прав
// кидает исключение прямо из logger'а и роняет обработку запроса/консьюмера.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Своё хранилище: собственная БД vacation_reporting (read-модель CQRS).
builder.Services.AddReportingStorage(builder.Configuration);

// Self-documentation: Swagger API этого сервиса.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// JWT (тот же токен, что выдаёт api).
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt["Issuer"],
            ValidateAudience = true, ValidAudience = jwt["Audience"],
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"] ?? "dev-only-secret-key-change-me-0123456789-0123456789")),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

var rabbit = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<LeaveRequestSubmittedConsumer>();
    x.AddConsumer<LeaveRequestStatusChangedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "vac");
            h.Password(rabbit["Password"] ?? "vac");
        });
        // Явные имена очередей: у notifier такие же консьюмеры, иначе MassTransit
        // дал бы ОДНУ очередь на всех (конкурентное потребление = потеря событий).
        cfg.ReceiveEndpoint("reporting-leave-submitted", e => e.ConfigureConsumer<LeaveRequestSubmittedConsumer>(ctx));
        cfg.ReceiveEndpoint("reporting-leave-status-changed", e => e.ConfigureConsumer<LeaveRequestStatusChangedConsumer>(ctx));
    });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
    EnsureDatabase.EnsureCreated(builder.Configuration.GetConnectionString("Reporting")!);
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "VacationPlanner.Reporting v1"));
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok("Healthy"));
app.MapGet("/health/ready", (ReportingDbContext db) => db.ReadLeaves.Any() ? Results.Ok("Healthy") : Results.StatusCode(503));
app.MapGet("/api/info", () => Results.Ok(ServiceInfo()));

// Read-модель (CQRS): календарь отпусков отдела из СВОЕЙ БД.
app.MapGet("/api/reports/calendar", async (int? year, ReportingDbContext db, CancellationToken ct) =>
{
    var y = year ?? DateTime.UtcNow.Year;
    var statuses = new[] { (int)LeaveStatus.Submitted, (int)LeaveStatus.Approved };

    var leaves = await db.ReadLeaves.AsNoTracking()
        .Where(r => statuses.Contains((int)r.Status) && r.StartDate.Year <= y && r.EndDate.Year >= y)
        .OrderBy(r => r.StartDate).ThenBy(r => r.UserId)
        .ToListAsync(ct);

    var overlaps = ComputeOverlaps(leaves, y);
    return Results.Ok(new
    {
        year = y,
        leaves = leaves.Select(l => new
        {
            l.Id, l.RequestId, l.UserId, l.UserName, l.Type, l.StartDate, l.EndDate, l.Status, l.Days
        }),
        overlaps
    });
}).RequireAuthorization();

app.Run();

static object ServiceInfo()
{
    var asm = typeof(Program).Assembly;
    return new
    {
        service = "reporting",
        version = asm.GetName().Version?.ToString() ?? "0.0.0",
        informationalVersion = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
        swagger = "/swagger",
        health = "/health/ready",
        storage = "database: vacation_reporting"
    };
}

static object ComputeOverlaps(List<VacationPlanner.Domain.Entities.ReadLeave> leaves, int year)
{
    var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
    var dayUids = new HashSet<int>[daysInYear];
    for (var i = 0; i < daysInYear; i++) dayUids[i] = new HashSet<int>();
    var y0 = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    foreach (var l in leaves)
    {
        if (l.Status != LeaveStatus.Submitted && l.Status != LeaveStatus.Approved) continue;
        var sf = l.StartDate.Date; var se = l.EndDate.Date;
        var a = Math.Max(0, (int)Math.Floor((sf - y0.Date).TotalDays));
        var b = Math.Min(daysInYear - 1, (int)Math.Floor((se - y0.Date).TotalDays));
        for (var i = a; i <= b; i++) dayUids[i].Add(l.UserId);
    }

    var byMonth = new int[12];
    var total = 0;
    for (var i = 0; i < daysInYear; i++)
        if (dayUids[i].Count > 1) { byMonth[y0.AddDays(i).Month - 1]++; total++; }

    string[] names = { "Январь","Февраль","Март","Апрель","Май","Июнь","Июль","Август","Сентябрь","Октябрь","Ноябрь","Декабрь" };
    var months = new List<object>();
    for (var m = 0; m < 12; m++) if (byMonth[m] > 0) months.Add(new { name = names[m], days = byMonth[m] });
    return new { totalDays = total, byMonth = months };
}
