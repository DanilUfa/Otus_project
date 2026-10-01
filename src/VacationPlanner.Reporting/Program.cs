using System.Text;
using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VacationPlanner.Application;
using VacationPlanner.Infrastructure;
using VacationPlanner.Reporting.Consumers;
using VacationPlanner.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "VacationPlanner.Reporting v1"));
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok("Healthy"));
app.MapGet("/health/ready", (AppDbContext db) => db.Users.Any() ? Results.Ok("Healthy") : Results.StatusCode(503));

// Идентификация сервиса (типичный endpoint реального продукта).
app.MapGet("/api/info", () => Results.Ok(ServiceInfo("reporting")));

// Read-модель (CQRS): календарь отпусков отдела из денормализованной таблицы ReadLeaves.
app.MapGet("/api/reports/calendar", async (int? year, AppDbContext db, CancellationToken ct) =>
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

static object ServiceInfo(string name)
{
    var asm = typeof(Program).Assembly;
    return new
    {
        service = name,
        version = asm.GetName().Version?.ToString() ?? "0.0.0",
        informationalVersion = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
        swagger = "/swagger",
        health = "/health/ready"
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
