using VacationPlanner.Observability;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VacationPlanner.Infrastructure;
using VacationPlanner.Notifier.Consumers;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://127.0.0.1:9401");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddServiceErrorMetrics();

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
        cfg.UseMessageRetry(r => r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
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
host.UseServiceMetrics();
await host.RunAsync();
