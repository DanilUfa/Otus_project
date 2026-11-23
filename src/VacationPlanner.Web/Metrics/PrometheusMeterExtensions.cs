using System.Diagnostics.Metrics;
using Prometheus;
using VacationPlanner.Application.Metrics;

namespace VacationPlanner.Web;

/// <summary>Business counters share a registry with HTTP and process metrics.</summary>
public static class PrometheusMeterExtensions
{
    public static IApplicationBuilder MapPrometheusMetrics(this WebApplication app, VacationMetrics metrics)
    {
        var counters = new Dictionary<Instrument, Counter>
        {
            [metrics.LeavesSubmitted] = Metrics.CreateCounter("vacation_leaves_submitted_total", "Total leave requests submitted"),
            [metrics.LeavesApproved] = Metrics.CreateCounter("vacation_leaves_approved_total", "Total leave requests approved"),
            [metrics.LeavesDeclined] = Metrics.CreateCounter("vacation_leaves_declined_total", "Total leave requests declined")
        };
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, instance) =>
        {
            if (counters.ContainsKey(instrument)) instance.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => counters[instrument].Inc(value));
        listener.Start();
        app.Lifetime.ApplicationStopped.Register(listener.Dispose);
        app.MapMetrics();
        return app;
    }
}
