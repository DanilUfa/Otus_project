using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace VacationPlanner.Web.Hubs;

/// <summary>
/// Realtime updates: private requests use u:{id}; the shared calendar uses CalendarGroup.
/// JWT middleware authenticates headers and the hub-scoped access_token query parameter.
/// Anonymous connections join neither group and receive no events.
/// </summary>
public class VacationHub : Hub
{
    public const string CalendarGroup = "calendar";

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        if (Context.User?.Identity?.IsAuthenticated != true) return;
        if (!int.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, "u:" + userId);
        await Groups.AddToGroupAsync(Context.ConnectionId, CalendarGroup);
    }
}
