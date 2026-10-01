using System.Text;
using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VacationPlanner.Audit.Consumers;
using VacationPlanner.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Своё хранилище: собственная БД vacation_audit (без доступа к данным api).
builder.Services.AddAuditStorage(builder.Configuration);

// Self-documentation: Swagger API этого сервиса.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// JWT (валидируем тот же токен, что выдали в api).
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"] ?? "dev-only-secret-key-change-me-0123456789-0123456789")),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// Слушаем события шины и пишем аудит в свою БД.
var rabbit = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<LeaveSubmittedAuditConsumer>();
    x.AddConsumer<LeaveStatusAuditConsumer>();
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
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    EnsureDatabase.EnsureCreated(builder.Configuration.GetConnectionString("Audit")!);
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "VacationPlanner.Audit v1"));
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok("Healthy"));
app.MapGet("/health/ready", (AuditDbContext db) => db.AuditLogs.Any() ? Results.Ok("Healthy") : Results.StatusCode(503));
app.MapGet("/api/info", () => Results.Ok(ServiceInfo()));

// История операций над заявками (bounded-контекст аудита, своя БД).
app.MapGet("/api/audit", async (int? requestId, int? userId, int? limit, AuditDbContext db, CancellationToken ct) =>
{
    var q = db.AuditLogs.AsNoTracking().AsQueryable();
    if (requestId.HasValue) q = q.Where(a => a.RequestId == requestId.Value);
    if (userId.HasValue) q = q.Where(a => a.UserId == userId.Value);
    q = q.OrderByDescending(a => a.CreatedAt);
    var rows = await q.Take(Math.Clamp(limit ?? 100, 1, 1000)).ToListAsync(ct);
    return Results.Ok(rows.Select(a => new { a.Id, a.RequestId, a.UserId, a.Action, a.Detail, a.CreatedAt }));
}).RequireAuthorization();

app.Run();

static object ServiceInfo()
{
    var asm = typeof(Program).Assembly;
    return new
    {
        service = "audit",
        version = asm.GetName().Version?.ToString() ?? "0.0.0",
        informationalVersion = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
        swagger = "/swagger",
        health = "/health/ready",
        storage = "database: vacation_audit"
    };
}
