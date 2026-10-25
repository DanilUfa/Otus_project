using System.Text;
using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VacationPlanner.Audit.Consumers;
using System.Security.Claims;
using VacationPlanner.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
// Логирование в Console (как в контейнере): EventLog на Windows без прав
// кидает исключение прямо из logger'а и роняет обработку запроса/консьюмера.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

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
                Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key не задан."))),
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
    x.AddConsumer<LeaveAttachmentAuditConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "vac");
            h.Password(rabbit["Password"] ?? "vac");
        });
        cfg.ReceiveEndpoint("audit-leave-submitted", e => e.ConfigureConsumer<LeaveSubmittedAuditConsumer>(ctx));
        cfg.ReceiveEndpoint("audit-leave-status-changed", e => e.ConfigureConsumer<LeaveStatusAuditConsumer>(ctx));
        cfg.ReceiveEndpoint("audit-leave-attachment-added", e => e.ConfigureConsumer<LeaveAttachmentAuditConsumer>(ctx));
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
// Readiness = связность с БД, а не наличие данных (иначе свежий деплой "не готов" навсегда).
app.MapGet("/health/ready", async (AuditDbContext db, CancellationToken ct) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
    return Results.Ok("Healthy");
});
app.MapGet("/api/info", () => Results.Ok(ServiceInfo()));

// История операций над заявками (bounded-контекст аудита, своя БД).
app.MapGet("/api/audit", async (int? requestId, int? userId, int? limit, ClaimsPrincipal user, AuditDbContext db, CancellationToken ct) =>
{
    var q = db.AuditLogs.AsNoTracking().AsQueryable();

    // Сотрудник видит ТОЛЬКО свои операции; вся история доступна руководителю/админу.
    var role = user.FindFirst(ClaimTypes.Role)?.Value;
    var ownId = int.Parse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    if (role is not ("Manager" or "Admin"))
    {
        q = q.Where(a => a.UserId == ownId);
        userId = ownId; // параметр userId для сотрудника бессмыслен — снижается до него самого
    }

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
