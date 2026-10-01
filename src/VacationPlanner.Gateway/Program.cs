using System.Net.WebSockets;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Единая точка входа (API Gateway). Маршрутизация по префиксам.
var api      = builder.Configuration["Upstreams:Api"]      ?? "http://127.0.0.1:5100";
var reporting= builder.Configuration["Upstreams:Reporting"]?? "http://127.0.0.1:5200";
var audit    = builder.Configuration["Upstreams:Audit"]    ?? "http://127.0.0.1:5300";

static string PathAndQuery(HttpContext ctx)
    => (ctx.Request.PathBase + ctx.Request.Path + ctx.Request.QueryString).ToString();

Uri Upstream(HttpContext ctx)
{
    var p = ctx.Request.Path.Value ?? "/";
    if (p.StartsWith("/api/reports/", StringComparison.OrdinalIgnoreCase)) return new Uri(reporting);
    if (p.StartsWith("/api/audit/", StringComparison.OrdinalIgnoreCase)) return new Uri(audit);
    return new Uri(api);
}

// Корреляционный id: сквозь всю цепочку сервисов + лог каждого запроса.
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    if (!ctx.Request.Headers.ContainsKey("X-Correlation-Id"))
        ctx.Request.Headers["X-Correlation-Id"] = Guid.NewGuid().ToString("N");
    ctx.Response.Headers["X-Correlation-Id"] = ctx.Request.Headers["X-Correlation-Id"].ToString();

    await next();

    sw.Stop();
    app.Logger.LogInformation(
        "{Method} {Path} -> {Status} {Elapsed}ms corr={CorrelationId}",
        ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds,
        ctx.Response.Headers["X-Correlation-Id"].ToString());
});

// Health контракт gateway (для docker healthcheck / балансировщика).
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "gateway" }));
app.MapGet("/health/ready", async (CancellationToken ct) =>
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var targets = new[] { ("api", api), ("reporting", reporting), ("audit", audit) };
    var deps = new List<object>();
    var allOk = true;
    foreach (var (name, url) in targets)
    {
        var ok = false;
        try { ok = (await probe.GetAsync(url + "/health/ready", ct)).IsSuccessStatusCode; } catch { ok = false; }
        allOk &= ok;
        deps.Add(new { name, url, ok });
    }
    return allOk
        ? Results.Ok(new { status = "Healthy", service = "gateway", dependencies = deps })
        : Results.Json(new { status = "Unhealthy", service = "gateway", dependencies = deps }, statusCode: 503);
});
app.MapGet("/api/info", () => Results.Ok(new
{
    service = "gateway",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    routes = new[]
    {
        new { prefix = "/api/reports", target = reporting, note = "reporting (CQRS read-model)" },
        new { prefix = "/api/audit",   target = audit,    note = "audit (audit trail)" },
        new { prefix = "*",            target = api,      note = "REST api, SPA-статика, SignalR /hubs" }
    }
}));
// Сама таблица маршрутов — наглядный инструмент демо.
app.MapGet("/gateway/routes", () => Results.Ok(new
{
    gateway = "VacationPlanner.Gateway",
    upstreams = new[]
    {
        new { prefix = "/api/reports", target = reporting, service = "reporting (CQRS read-model)", example = "GET /api/reports/calendar?year=2026" },
        new { prefix = "/api/audit",   target = audit,    service = "audit (audit trail / Memento)", example = "GET /api/audit?requestId=5" },
        new { prefix = "*",            target = api,      service = "api (write-side, SPA, /hubs SignalR ws)", example = "POST /api/leave · GET /api/balance/2?year=2026" }
    }
}));

// Терминальный реверс-прокси (fallback для всех остальных путей).
app.Run(async ctx =>
{
    var target = Upstream(ctx);
    if (ctx.WebSockets.IsWebSocketRequest)
    {
        await ProxyWebSocketAsync(ctx, target, ctx.RequestAborted);
        return;
    }
    await ProxyHttpAsync(ctx, target, ctx.RequestAborted);
});

app.Run();

static async Task ProxyHttpAsync(HttpContext ctx, Uri target, CancellationToken ct)
{
    using var client = new HttpClient();
    client.Timeout = Timeout.InfiniteTimeSpan;

    using var upstream = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), new Uri(target, PathAndQuery(ctx)));

    foreach (var h in ctx.Request.Headers)
    {
        if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
        if (h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
        if (!upstream.Headers.TryAddWithoutValidation(h.Key, (IEnumerable<string>)h.Value))
            upstream.Content?.Headers.TryAddWithoutValidation(h.Key, (IEnumerable<string>)h.Value);
    }
    if (ctx.Request.ContentLength is > 0)
        upstream.Content = new StreamContent(ctx.Request.Body);

    using var resp = await client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, ct);

    ctx.Response.StatusCode = (int)resp.StatusCode;
    foreach (var h in resp.Headers)       ctx.Response.Headers.TryAdd(h.Key, string.Join(",", h.Value));
    foreach (var h in resp.Content.Headers) ctx.Response.Headers.TryAdd(h.Key, string.Join(",", h.Value));
    await resp.Content.CopyToAsync(ctx.Response.Body, ct);
}

static async Task ProxyWebSocketAsync(HttpContext ctx, Uri target, CancellationToken ct)
{
    var wsUri = new Uri(target, PathAndQuery(ctx)).ToString()
        .Replace("http://", "ws://").Replace("https://", "wss://");
    using var client = new ClientWebSocket();
    foreach (var h in ctx.Request.Headers)
    {
        if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
        try { client.Options.SetRequestHeader(h.Key, h.Value.ToString()); } catch { /* ignore restricted */ }
    }
    try { await client.ConnectAsync(new Uri(wsUri), ct); }
    catch { ctx.Response.StatusCode = 502; await ctx.Response.WriteAsync("ws upstream unavailable", ct); return; }

    using var server = await ctx.WebSockets.AcceptWebSocketAsync();
    await Task.WhenAll(PumpAsync(server, client, ct), PumpAsync(client, server, ct));
}

static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
{
    var buffer = new byte[81920];
    try
    {
        while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
        {
            var res = await from.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (res.MessageType == WebSocketMessageType.Close)
            {
                if (to.State == WebSocketState.Open)
                    await to.CloseAsync(res.CloseStatus ?? WebSocketCloseStatus.NormalClosure, res.CloseStatusDescription, ct);
                return;
            }
            await to.SendAsync(new ArraySegment<byte>(buffer, 0, res.Count), res.MessageType, res.EndOfMessage, ct);
        }
    }
    catch { /* best-effort duplex */ }
}
