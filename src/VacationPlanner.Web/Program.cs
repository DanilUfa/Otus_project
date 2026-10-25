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
using VacationPlanner.Web.Auth;
using VacationPlanner.Web.Caching;
using VacationPlanner.Web.Health;
using VacationPlanner.Web.Hubs;
using VacationPlanner.Web.Realtime;
using User = VacationPlanner.Domain.Entities.User;

var builder = WebApplication.CreateBuilder(args);
// Логирование в Console (как в контейнере): EventLog на Windows без прав
// кидает исключение прямо из logger'а и роняет обработку запроса/консьюмера.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// --- SPA static root: wwwroot каталога проекта Web ---
// В dev (dotnet run) папка лежит в каталоге проекта (bin/Debug/net10.0/../../..),
// в publish/контейнере — рядом с точкой входа (/app/wwwroot). Берём существующий,
// чтобы раздача SPA работала и в dev, и в опубликованной сборке.
var webRootCandidates = new[]
{
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),                  // publish / контейнер
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot") // dotnet run (dev)
};
var webRoot = webRootCandidates.FirstOrDefault(Directory.Exists)
    ?? throw new InvalidOperationException(
        "Каталог wwwroot не найден. Кандидаты: " + string.Join(", ", webRootCandidates));
builder.WebHost.UseWebRoot(webRoot);

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
// --- Transaction outbox: фоновый отправитель событий в шину ---
builder.Services.AddHostedService<OutboxPublisherHostedService>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
builder.Services.AddSingleton<LoginThrottle>();

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

// SPA (wwwroot): раздача статики + index.html по умолчанию.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Доменные исключения -> HTTP-коды (до эндпоинтов; 500 для прочего).
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DomainValidationException ex) // бизнес-валидация (даты, пересечения)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (NotFoundException ex) // сущность не найдена
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException) // нет прав на действие (не автор/не рук-ль)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { error = "Доступ запрещён: недостаточно прав." });
    }
    catch (InvalidOperationException ex) // нелегальный State-переход
    {
        ctx.Response.StatusCode = StatusCodes.Status409Conflict;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

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
api.MapPost("/auth/login", async (LoginRequest request, AuthService auth, LoginThrottle throttle, CancellationToken ct)
        => {
            if (!throttle.TryEnter(request.Email))
                return Results.Json(new { error = "Слишком много попыток входа. Подождите 15 минут." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            try
            {
                var response = await auth.LoginAsync(request, ct);
                throttle.Reset(request.Email);
                return Results.Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                throttle.RegisterFailure(request.Email);
                return Results.Json(new { error = "Неверный email или пароль." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        })
   .WithName("Login")
   ;

// Регистрация: код подтверждения отправляется на почту (код живёт в Redis-кэше с TTL 10 мин).
api.MapPost("/auth/register", async (RegisterRequest request, RegistrationService reg, CancellationToken ct)
        => {
            await reg.RequestAsync(request, ct);
            return Results.Ok(new { message = "Код подтверждения отправлен на вашу почту." });
        })
   .DisableAntiforgery()
   .WithName("Register")
   ;

// Подтверждение кодом из письма: создаёт пользователя и сразу возвращает пару access+refresh.
api.MapPost("/auth/register/confirm", async (ConfirmRegistrationRequest request, RegistrationService reg, AuthService auth, CancellationToken ct)
        => Results.Ok(await auth.IssueInitialPairAsync(await reg.ConfirmAsync(request, ct), ct)))
   .DisableAntiforgery()
   .WithName("ConfirmRegistration")
   ;

// Повторная отправка кода: не чаще раза в минуту (коoldown в Redis).
api.MapPost("/auth/register/resend", async (ResendRegistrationRequest request, RegistrationService reg, CancellationToken ct)
        => {
            await reg.ResendAsync(request, ct);
            return Results.Ok(new { message = "Новый код отправлен на вашу почту." });
        })
   .DisableAntiforgery()
   .WithName("ResendRegistrationCode")
   ;

// Ротация refresh-токена: старый отзывается, выдаётся новая пара access+refresh.
api.MapPost("/auth/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct)
        => {
            try { return Results.Ok(await auth.RefreshAsync(request.RefreshToken, ct)); }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        })
   .WithName("Refresh")
   ;

// Logout: отзыв refresh-токена (access — stateless, живёт до истечения). Идемпотентно.
api.MapPost("/auth/logout", async (RefreshRequest request, AuthService auth, CancellationToken ct)
        => { await auth.LogoutAsync(request.RefreshToken, ct); return Results.NoContent(); })
   .WithName("Logout")
   ;

// ---------- Users ----------
api.MapGet("/users", async (IUserRepository users, IDistributedCache cache, ClaimsPrincipal user, CancellationToken ct)
        => {
            var all = await cache.GetOrSetAsync("users",
                async () => (await users.GetAllAsync(ct))
                    .Select(u => new UserDto(u.Id, u.Name, u.Email, u.Role.ToString()))
                    .ToList(),
                TimeSpan.FromSeconds(30), ct);

            // График отпусков отдела — общий (офисная доска): состав сотрудников виден всем.
            // Сами заявки остаются приватными: сотрудник видит только свои (см. /api/leave).
            return Results.Ok(all);
        })
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetUsers")
   ;

// ---------- Leave requests ----------
api.MapGet("/leave", async (LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => Results.Ok(user.IsInRole(UserRole.Manager.ToString())
            ? await service.GetAllAsync(ct)
            : await service.GetAllForUserAsync(GetUserId(user), ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetLeaveRequests")
   ;

api.MapPost("/leave", async (CreateLeaveRequestRequest request, LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => Results.Created("/api/leave", await service.SubmitAsync(request with { UserId = GetUserId(user) }, ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("SubmitLeaveRequest")
   ;

api.MapPost("/leave/draft", async (CreateLeaveRequestRequest request, LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => Results.Created("/api/leave", await service.SaveDraftAsync(request with { UserId = GetUserId(user) }, ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("SaveLeaveDraft")
   ;

api.MapPost("/leave/{id:int}/submit", async (int id, LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => Results.Ok(await service.SubmitDraftAsync(id, GetUserId(user), ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("SubmitLeaveDraft")
   ;

api.MapPost("/leave/{id:int}/cancel", async (int id, LeaveService service, ClaimsPrincipal user, CancellationToken ct)
        => { await service.CancelAsync(id, GetUserId(user), ct); return Results.NoContent(); })
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("CancelLeaveRequest")
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

// ---------- Attachments (документы к заявке: больничный лист, справка) ----------
api.MapPost("/leave/{id:int}/attachments", async (int id, IFormFile file, AttachmentService service, ClaimsPrincipal user, CancellationToken ct)
        => {
            var dto = await service.AddAsync(id, GetUserId(user), file.FileName, file.ContentType, file.Length, file.OpenReadStream(), ct);
            return Results.Created($"/api/leave/attachments/{dto.Id}", dto);
        })
   .RequireAuthorization("AuthenticatedOnly")
   .DisableAntiforgery()
   .WithName("UploadLeaveAttachment")
   ;

api.MapGet("/leave/{id:int}/attachments", async (int id, AttachmentService service, ClaimsPrincipal user, CancellationToken ct)
        => Results.Ok(await service.GetAttachmentsAsync(id, GetUserId(user), IsManagerOrAdmin(user), ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetLeaveAttachments")
   ;

api.MapGet("/leave/attachments/{attachmentId:int}/download", async (int attachmentId, AttachmentService service, ClaimsPrincipal user, CancellationToken ct)
        => {
            var dl = await service.DownloadAsync(attachmentId, GetUserId(user), IsManagerOrAdmin(user), ct);
            return Results.File(dl.Content, dl.ContentType, dl.FileName);
        })
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("DownloadLeaveAttachment")
   ;

api.MapDelete("/leave/attachments/{attachmentId:int}", async (int attachmentId, AttachmentService service, ClaimsPrincipal user, CancellationToken ct)
        => { await service.DeleteAsync(attachmentId, GetUserId(user), IsManagerOrAdmin(user), ct); return Results.NoContent(); })
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("DeleteLeaveAttachment")
   ;

// ---------- Праздничные дни (справочник для календаря) ----------
api.MapGet("/holidays", async (int? year, AppDbContext db, CancellationToken ct)
        => Results.Ok(await db.Holidays
            .Where(h => h.Date.Year == (year ?? DateTime.UtcNow.Year))
            .OrderBy(h => h.Date)
            .Select(h => new { date = h.Date, name = h.Name, kind = h.Kind })
            .ToListAsync(ct)))
   .RequireAuthorization("AuthenticatedOnly")
   .WithName("GetHolidays")
   ;

// ---------- Balance ----------
api.MapGet("/balance/{userId:int}", async (
        int userId, [FromQuery] int? year, BalanceService balance, IDistributedCache cache,
        ClaimsPrincipal user, CancellationToken ct)
        => {
            if (!user.IsInRole(UserRole.Manager.ToString()) && GetUserId(user) != userId)
                throw new UnauthorizedAccessException("Запрос баланса другого сотрудника доступен руководителю.");

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
// SPA по умолчанию на "/" (Swagger остаётся на /swagger — ссылка в форме входа).
app.MapGet("/", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
app.MapFallbackToFile("index.html");

app.Run();

static int GetUserId(ClaimsPrincipal user)
    => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());

static bool IsManagerOrAdmin(ClaimsPrincipal user)
    => user.IsInRole(UserRole.Manager.ToString()) || user.IsInRole(UserRole.Admin.ToString());

// Тестовые данные для черновика. Идиопотентно: пользователи добавляются, если отсутствуют.
static void Seed(AppDbContext db, IPasswordHasher hasher)
{
    var byEmail = db.Users.ToDictionary(u => u.Email);

    User Ensure(string email, string password, string name, UserRole role, User? manager = null)
    {
        if (byEmail.TryGetValue(email, out var existing)) return existing;
        var u = new User { Name = name, Email = email, Role = role, PasswordHash = hasher.Hash(password), Manager = manager };
        db.Users.Add(u);
        return u;
    }

    var manager = Ensure("manager@vac.local", "Manager123!", "Иван Менеджер", UserRole.Manager);
    // Сотрудники для демо «графика отдела» / пересечений.
    Ensure("petr@vac.local", "User123!", "Пётр Сотрудник", UserRole.Employee, manager);
    Ensure("maria@vac.local", "User123!", "Мария Бухгалтер", UserRole.Employee, manager);
    Ensure("alex@vac.local", "User123!", "Алексей Инженер", UserRole.Employee, manager);

    // Производственный календарь РФ 2025-2028 (источник: assistentus.ru/proizvodstvennyj-kalendar-*).
    // Kind: 0 — нерабочий праздничный день, 1 — перенос выходного, 2 — сокращённый предпраздничный день.
    // Это справочник для отображения в календаре; расчёт отпусков его не учитывает (дни по выбранному периоду).
    if (!db.Holidays.Any())
    {
        var holidayDefs = new (string Md, string Name, int Kind)[]
        {
            // 2025 — праздники
            ("2025-01-01", "Новогодние каникулы", 0), ("2025-01-02", "Новогодние каникулы", 0), ("2025-01-03", "Новогодние каникулы", 0), ("2025-01-04", "Новогодние каникулы", 0), ("2025-01-05", "Новогодние каникулы", 0), ("2025-01-06", "Новогодние каникулы", 0), ("2025-01-07", "Рождество Христово", 0), ("2025-01-08", "Новогодние каникулы", 0), ("2025-02-23", "День защитника Отечества", 0), ("2025-03-08", "Международный женский день", 0), ("2025-05-01", "Праздник Весны и Труда", 0), ("2025-05-09", "День Победы", 0), ("2025-06-12", "День России", 0), ("2025-11-04", "День народного единства", 0),
            // 2025 — переносы и сокращённые дни
            ("2025-05-02", "Перенос (доп. выходной)", 1), ("2025-05-08", "Перенос (доп. выходной)", 1), ("2025-06-13", "Перенос (доп. выходной)", 1), ("2025-11-03", "Перенос (доп. выходной)", 1), ("2025-03-07", "Сокращённый предпраздничный день", 2), ("2025-04-30", "Сокращённый предпраздничный день", 2), ("2025-06-11", "Сокращённый предпраздничный день", 2), ("2025-11-01", "Сокращённый предпраздничный день", 2),
            // 2026 — праздники
            ("2026-01-01", "Новогодние каникулы", 0), ("2026-01-02", "Новогодние каникулы", 0), ("2026-01-03", "Новогодние каникулы", 0), ("2026-01-04", "Новогодние каникулы", 0), ("2026-01-05", "Новогодние каникулы", 0), ("2026-01-06", "Новогодние каникулы", 0), ("2026-01-07", "Рождество Христово", 0), ("2026-01-08", "Новогодние каникулы", 0), ("2026-02-23", "День защитника Отечества", 0), ("2026-03-08", "Международный женский день", 0), ("2026-05-01", "Праздник Весны и Труда", 0), ("2026-05-09", "День Победы", 0), ("2026-06-12", "День России", 0), ("2026-11-04", "День народного единства", 0),
            // 2026 — переносы и сокращённые дни
            ("2026-01-09", "Перенос (доп. выходной)", 1), ("2026-03-09", "Перенос (доп. выходной)", 1), ("2026-05-11", "Перенос (доп. выходной)", 1), ("2026-12-31", "Перенос (доп. выходной)", 1), ("2026-04-30", "Сокращённый предпраздничный день", 2), ("2026-05-08", "Сокращённый предпраздничный день", 2), ("2026-06-11", "Сокращённый предпраздничный день", 2), ("2026-11-03", "Сокращённый предпраздничный день", 2),
            // 2027 — праздники
            ("2027-01-01", "Новогодние каникулы", 0), ("2027-01-02", "Новогодние каникулы", 0), ("2027-01-03", "Новогодние каникулы", 0), ("2027-01-04", "Новогодние каникулы", 0), ("2027-01-05", "Новогодние каникулы", 0), ("2027-01-06", "Новогодние каникулы", 0), ("2027-01-07", "Рождество Христово", 0), ("2027-01-08", "Новогодние каникулы", 0), ("2027-02-23", "День защитника Отечества", 0), ("2027-03-08", "Международный женский день", 0), ("2027-05-01", "Праздник Весны и Труда", 0), ("2027-05-09", "День Победы", 0), ("2027-06-12", "День России", 0), ("2027-11-04", "День народного единства", 0),
            // 2027 — переносы и сокращённые дни
            ("2027-02-22", "Перенос (доп. выходной)", 1), ("2027-05-03", "Перенос (доп. выходной)", 1), ("2027-05-10", "Перенос (доп. выходной)", 1), ("2027-06-14", "Перенос (доп. выходной)", 1), ("2027-11-05", "Перенос (доп. выходной)", 1), ("2027-12-31", "Перенос (доп. выходной)", 1), ("2027-02-20", "Сокращённый предпраздничный день", 2), ("2027-04-30", "Сокращённый предпраздничный день", 2), ("2027-06-11", "Сокращённый предпраздничный день", 2), ("2027-11-03", "Сокращённый предпраздничный день", 2),
            // 2028 — праздники
            ("2028-01-01", "Новогодние каникулы", 0), ("2028-01-02", "Новогодние каникулы", 0), ("2028-01-03", "Новогодние каникулы", 0), ("2028-01-04", "Новогодние каникулы", 0), ("2028-01-05", "Новогодние каникулы", 0), ("2028-01-06", "Новогодние каникулы", 0), ("2028-01-07", "Рождество Христово", 0), ("2028-02-23", "День защитника Отечества", 0), ("2028-03-08", "Международный женский день", 0), ("2028-05-01", "Праздник Весны и Труда", 0), ("2028-05-09", "День Победы", 0), ("2028-06-12", "День России", 0), ("2028-11-04", "День народного единства", 0),
            // 2028 — переносы и сокращённые дни
            ("2028-05-02", "Перенос (доп. выходной)", 1), ("2028-05-08", "Перенос (доп. выходной)", 1), ("2028-11-06", "Перенос (доп. выходной)", 1), ("2028-02-22", "Сокращённый предпраздничный день", 2), ("2028-03-07", "Сокращённый предпраздничный день", 2), ("2028-11-03", "Сокращённый предпраздничный день", 2),
        };
        foreach (var (date, name, kind) in holidayDefs)
            db.Holidays.Add(new Holiday
            {
                Date = DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal),
                Name = name,
                Kind = kind,
            });
        db.SaveChanges();
    }
}