using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace VacationPlanner.Observability;

public static class ServiceMetrics
{
    public static ILoggingBuilder AddServiceErrorMetrics(this ILoggingBuilder logging)
        => logging.AddProvider(new ErrorMetricsLoggerProvider());

    public static WebApplication UseServiceMetrics(this WebApplication app, bool mapEndpoint = true)
    {
        app.UseRouting();
        app.UseHttpMetrics();
        // Preserve mapped HTTP errors; count unhandled exceptions as server failures.
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch
            {
                if (!context.Response.HasStarted) context.Response.StatusCode = 500;
                throw;
            }
        });
        if (mapEndpoint) app.MapMetrics();
        return app;
    }
}

internal sealed class ErrorMetricsLoggerProvider : ILoggerProvider
{
    private static readonly Counter Events = Metrics.CreateCounter(
        "vacation_log_events_total", "Warning, error and critical log events in this process.",
        new CounterConfiguration { LabelNames = ["level"] });

    public ErrorMetricsLoggerProvider()
    {
        foreach (var level in new[] { "warning", "error", "critical" })
            Events.WithLabels(level).Inc(0);
    }

    public ILogger CreateLogger(string categoryName) => new ErrorMetricsLogger();
    public void Dispose() { }

    private sealed class ErrorMetricsLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Warning and <= LogLevel.Critical;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Events.WithLabels(logLevel.ToString().ToLowerInvariant()).Inc();
        }
    }
}
