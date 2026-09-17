using System.Diagnostics.Metrics;

namespace VacationPlanner.Application.Metrics;

/// <summary>
/// Бизнес-метрики через стандартный .NET API <see cref="Meter"/> (System.Diagnostics.Metrics).
/// Счётчики инкрементятся в сервисах; эндпоинт /metrics экспортирует их в формат Prometheus.
/// (Fork OpenTelemetry: Meter — общий контракт, экспортёр у нас свой, лёгкий.)
/// </summary>
public sealed class VacationMetrics
{
    private readonly Meter _meter = new("VacationPlanner", "1.0.0");

    public Counter<long> LeavesSubmitted { get; }
    public Counter<long> LeavesApproved { get; }
    public Counter<long> LeavesDeclined { get; }

    public VacationMetrics()
    {
        LeavesSubmitted = _meter.CreateCounter<long>(
            "vacation_leaves_submitted_total", "leaves", "Всего подано заявок на отпуск");
        LeavesApproved = _meter.CreateCounter<long>(
            "vacation_leaves_approved_total", "leaves", "Всего заявок утверждено руководителем");
        LeavesDeclined = _meter.CreateCounter<long>(
            "vacation_leaves_declined_total", "leaves", "Всего заявок отклонено руководителем");
    }
}