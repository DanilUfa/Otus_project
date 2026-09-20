using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using VacationPlanner.Application;
using VacationPlanner.Application.Contracts;
using VacationPlanner.Application.Metrics;
using VacationPlanner.Application.Services;
using VacationPlanner.Domain;
using VacationPlanner.Domain.Entities;
using VacationPlanner.Web;
using VacationPlanner.Domain.Events;
using VacationPlanner.Infrastructure;
using VacationPlanner.Infrastructure.Security;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VacationPlanner.Web.Caching;
using VacationPlanner.Web.Health;
using VacationPlanner.Web.Hubs;
using VacationPlanner.Web.Realtime;
using User = VacationPlanner.Domain.Entities.User;

var builder = WebApplication.CreateBuilder(args);

// --- Swagger (Swashbuckle) ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Application + Infrastructure ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- JWT Authentication + Authorization ---
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ManagerOnly", p => p.RequireRole(UserRole.Manager.ToString()))
    .AddPolicy("AuthenticatedOnly", p => p.RequireAuthenticatedUser());

// --- MassTransit (RabbitMQ) ---
var rabbit = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "vac");
            h.Password(rabbit["Password"] ?? "vac");
        });
    });
});

// --- SignalR (realtime) ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();

// --- Redis (IDistributedCache) ---
var redisHost = builder.Configuration["Redis:Host"] ?? "localhost";
var redisPort = int.TryParse(builder.Configuration["Redis:Port"], out var rp) ? rp : 6379;
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = $"{redisHost}:{redisPort}");

// --- Health checks (readiness: db + rabbitmq + redis) ---
builder.Services.AddHealthChecks()
    .AddCheck<DbHealthCheck>("db")
    .AddCheck("rabbitmq", new TcpHealthCheck(rabbit["Host"] ?? "localhost", 5672, "rabbitmq"))
    .AddCheck("redis", new TcpHealthCheck(redisHost, redisPort, "redis"));

var app = builder.Build();

// Применяем миграции EF Core (схема управляется миграциями).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    db.Database.Migrate();
    Seed(db, hasher);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// Метрики для Prometheus — публичный эндпоинт /metrics.
app.MapPrometheusMetrics(app.Services.GetRequiredService<VacationMetrics>());

// Health: liveness (жив сам процесс) и readiness (готов отвечать: БД+Rabbit+Redis живы).
app.MapGet("/health/live", () => Results.Ok("Healthy"));
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Name is "db" or "rabbitmq" or "redis"
});

// SignalR-хаб для realtime-обновлений SPA.
app.MapHub<VacationHub>("/hubs/vacation");

var api = app.MapGroup("/api");

// ---------- Auth ----------
api.MapPost("/auth/login", async (LoginRequest request, AuthService auth, CancellationToken ct)
        => await auth.LoginAsync(request, ct))
   .WithName("Login")
   ;

// ---------- Users ----------
api.MapGet("/users", async (IUserRepository users, IDistributedCache cache, CancellationToken ct)
        => Results.Ok(await cache.GetOrSetAsync("users",
            async () => (await users.GetAllAsync(ct))
                .Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role.ToString()))
                .ToList(),
            TimeSpan.FromSeconds(30), ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetUsers")
   ;

// ---------- Leave requests ----------
api.MapGet("/leave", async (LeaveService service, CancellationToken ct)
        => Results.Ok(await service.GetAllAsync(ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetLeaveRequests")
   ;

api.MapPost("/leave", async (CreateLeaveRequestRequest request, LeaveService service, CancellationToken ct)
        => Results.Created("/api/leave", await service.SubmitAsync(request, ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("SubmitLeaveRequest")
   ;

api.MapPost("/leave/draft", async (CreateLeaveRequestRequest request, LeaveService service, CancellationToken ct)
        => Results.Created("/api/leave", await service.SaveDraftAsync(request, ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("SaveLeaveDraft")
   ;

api.MapPost("/leave/{id:int}/approve", async (int id, LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => {
            var approverId = GetUserId(user);
            await service.UpdateStatusAsync(id, LeaveStatus.Approved, approverId, ct);
            return Results.NoContent();
        })
   .RequireAuthorization("ManagerOnly")
   .WithName("ApproveLeaveRequest")
   ;

api.MapPost("/leave/{id:int}/decline", async (int id, LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => {
            var approverId = GetUserId(user);
            await service.UpdateStatusAsync(id, LeaveStatus.Declined, approverId, ct);
            return Results.NoContent();
        })
   .RequireAuthorization("ManagerOnly")
   .WithName("DeclineLeaveRequest")
   ;

// ---------- Balance ----------
api.MapGet("/balance/{userId:int}", async (
        int userId, [FromQuery] int? year, BalanceService balance, IDistributedCache cache,
        CancellationToken ct)
        => {
            var y = year ?? DateTime.UtcNow.Year;
            var remaining = await cache.GetOrSetAsync($"balance:{userId}:{y}",
                () => balance.GetRemainingAnnualDaysAsync(userId, y, ct),
                TimeSpan.FromSeconds(60), ct);
            return Results.Ok(new { year = y, remaining });
        })
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetRemainingBalance")
   ;

// ---------- UI / fallback ----------
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapFallbackToFile("index.html");

app.Run();

static int GetUserId(ClaimsPrincipal user)
    => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());

// Тестовые данные для черновика.
static void Seed(AppDbContext db, IPasswordHasher hasher)
{
    if (db.Users.Any()) return;

    var manager = new User
    {
        Name = "Иван Менеджер",
        Email = "manager@vac.local",
        Role = UserRole.Manager,
        PasswordHash = hasher.Hash("Manager123!")
    };
    var employee = new User
    {
        Name = "Пётр Сотрудник",
        Email = "petr@vac.local",
        Role = UserRole.Employee,
        Manager = manager,
        PasswordHash = hasher.Hash("User123!")
    };

    db.Users.AddRange(manager, employee);
    db.SaveChanges();
}