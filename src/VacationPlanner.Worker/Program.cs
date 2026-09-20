using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VacationPlanner.Application;
using VacationPlanner.Infrastructure;
using VacationPlanner.Worker.Consumers;
using VacationPlanner.Worker.HostedServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Фоновая политика: авто-закрытие «зависших» черновиков (через StaleDraftService + State).
builder.Services.AddHostedService<DraftCleanupHostedService>();

var rabbit = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<LeaveRequestSubmittedConsumer>();
    x.AddConsumer<LeaveRequestStatusChangedConsumer>();
    // Аудит-консьюмеры на те же события (fan-out: каждый endpoint получает копию).
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

var host = builder.Build();

// Worker тоже применяет миграции (идемпотентно — если API уже применил, ничего не делает).
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

await host.RunAsync();