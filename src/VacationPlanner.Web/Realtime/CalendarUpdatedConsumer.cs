using MassTransit;
using Microsoft.AspNetCore.SignalR;
using VacationPlanner.Domain.Events;
using VacationPlanner.Web.Hubs;

namespace VacationPlanner.Web.Realtime;

public class CalendarUpdatedConsumer(IHubContext<VacationHub> hub) : IConsumer<CalendarUpdated>
{
    public Task Consume(ConsumeContext<CalendarUpdated> context)
        => hub.Clients.Group(VacationHub.CalendarGroup).SendAsync("CalendarUpdated", context.Message,
            context.CancellationToken);
}
