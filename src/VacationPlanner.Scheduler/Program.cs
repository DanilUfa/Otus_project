using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VacationPlanner.Application;
using VacationPlanner.Infrastructure;
using VacationPlanner.Scheduler.HostedServices;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Фоновая политика: авто-закрытие «зависших» черновиков (State-переходы).
builder.Services.AddHostedService<DraftCleanupHostedService>();

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
await host.RunAsync();
