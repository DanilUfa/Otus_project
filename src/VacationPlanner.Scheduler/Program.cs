using VacationPlanner.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VacationPlanner.Application;
using VacationPlanner.Infrastructure;
using VacationPlanner.Scheduler.HostedServices;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://127.0.0.1:9402");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddServiceErrorMetrics();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Фоновые политики: авто-закрытие «зависших» черновиков (State-переходы)
// и чистка недействительных refresh-токенов.
builder.Services.AddHostedService<DraftCleanupHostedService>();
builder.Services.AddHostedService<RefreshTokenCleanupHostedService>();

var host = builder.Build();
host.UseServiceMetrics();
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
await host.RunAsync();
