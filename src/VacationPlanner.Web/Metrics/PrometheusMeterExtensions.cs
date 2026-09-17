using System.Diagnostics.Metrics;
using System.Text;
using VacationPlanner.Application.Metrics;

namespace VacationPlanner.Web;

/// <summary>
/// Эндпоинт <c>/metrics</c> в формате Prometheus (text/plain; version=0.0.4).
/// MeterListener копит Cumulative-значения наших Counter'ов; запрос рендерит их.
/// </summary>
public static class PrometheusMeterExtensions
{
    public static IApplicationBuilder MapPrometheusMetrics(this WebApplication app, VacationMetrics metrics)
    {
        var listener = new MeterListener();
        var totals = new Dictionary<Instrument, long>();

        listener.InstrumentPublished = (instrument, instance) =>
        {
            if (instrument.Meter.Name == "VacationPlanner")
                instance.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (totals)
            {
                totals.TryGetValue(instrument, out var t);
                totals[instrument] = t + value; // кумулятивный счётчик
            }
        });
        listener.Start();

        app.MapGet("/metrics", () =>
        {
            lock (totals)
            {
                var sb = new StringBuilder();
                AppendCounter(sb, "vacation_leaves_submitted_total", "Total leave requests submitted", totals, metrics.LeavesSubmitted);
                AppendCounter(sb, "vacation_leaves_approved_total", "Total leave requests approved", totals, metrics.LeavesApproved);
                AppendCounter(sb, "vacation_leaves_declined_total", "Total leave requests declined", totals, metrics.LeavesDeclined);
                return Results.Text(sb.ToString(), "text/plain; version=0.0.4");
            }
        });

        return app;
    }

    private static void AppendCounter(StringBuilder sb, string name, string help,
        Dictionary<Instrument, long> totals, Counter<long> counter)
    {
        sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        sb.Append("# TYPE ").Append(name).AppendLine(" counter");
        sb.Append(name).Append(' ').AppendLine(totals.GetValueOrDefault(counter).ToString("D"));
    }
}