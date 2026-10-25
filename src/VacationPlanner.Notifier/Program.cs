using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VacationPlanner.Infrastructure;
using VacationPlanner.Notifier.Consumers;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// notifier — stateless сервис: только уведомления, без собственной БД.
builder.Services.AddNotifications();

var rabbit = builder.Configuration.GetSection("RabbitMq");
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<LeaveRequestSubmittedConsumer>();
    x.AddConsumer<LeaveRequestStatusChangedConsumer>();
    x.AddConsumer<EmailVerificationConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "vac");
            h.Password(rabbit["Password"] ?? "vac");
        });
        cfg.ReceiveEndpoint("notifier-leave-submitted", e => e.ConfigureConsumer<LeaveRequestSubmittedConsumer>(ctx));
        cfg.ReceiveEndpoint("notifier-leave-status-changed", e => e.ConfigureConsumer<LeaveRequestStatusChangedConsumer>(ctx));
        cfg.ReceiveEndpoint("notifier-email-verification", e => e.ConfigureConsumer<EmailVerificationConsumer>(ctx));
    });
});

var host = builder.Build();
await host.RunAsync();
