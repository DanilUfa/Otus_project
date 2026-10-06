using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace VacationPlanner.Web.Hubs;

/// <summary>
/// SignalR-хаб для realtime-обновлений SPA (новые заявки, смена статуса).
/// Подключение аутентифицируется так же, как REST: токен берётся из query-параметра
/// access_token (клиент @microsoft/signalr добавляет его автоматически из accessTokenFactory).
/// Анонимные/невалидные подключения принимаются, но не входят ни в одну группу и не получают событий.
/// Получатель события — группа "u:{userId}": владелец заявки и, при создании, её руководитель.
/// </summary>
public class VacationHub : Hub
{
    private readonly TokenValidationParameters _validation;
    private readonly JwtSecurityTokenHandler _handler = new();

    public VacationHub(IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        _validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key не задан."))),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        var userId = Authenticate();
        if (userId is null) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, "u:" + userId);
    }

    private int? Authenticate()
    {
        var token = Context.GetHttpContext()?.Request.Query["access_token"].FirstOrDefault();
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            // Context.User в хабе read-only; нам нужен только userId для группы доставки.
            var principal = _handler.ValidateToken(token, _validation, out _);
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var value) ? value : null;
        }
        catch { return null; }
    }
}
