using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VacationPlanner.Importer.Csv;
using VacationPlanner.Importer.Http;
using Xunit;

namespace VacationPlanner.Importer.Tests;

public sealed class ApiTests
{
    private static readonly LeavePayload Payload = new(2, LeaveType.Unpaid, DateTime.UtcNow.Date, DateTime.UtcNow.Date, "");

    [Theory]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task SendsOnceAndClassifies(int status)
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(async (req, ct) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/api/leave", req.RequestUri!.AbsolutePath);
            var json = await req.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(2, json.GetProperty("type").GetInt32());
            return status == 201 ? DatabaseTests.Receipt() : new HttpResponseMessage((HttpStatusCode)status)
                { Content = JsonContent.Create(new { error = "Период пересекается с заявкой." }) };
        })) { BaseAddress = new Uri("http://test") };
        var api = new LeaveApi(http);
        if (status == 201) Assert.Equal(7, (await api.SubmitAsync(Payload, default)).LeaveId);
        else if (status == 400) Assert.Equal("Leave dates overlap.", (await api.SubmitAsync(Payload, default)).Error?.Message);
        else await Assert.ThrowsAsync<ApiFailure>(() => api.SubmitAsync(Payload, default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DistinguishesCancellationAndTimeout(bool cancel)
    {
        int calls = 0;
        using var stop = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            calls++;
            if (cancel) stop.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return DatabaseTests.Receipt();
        })) { BaseAddress = new Uri("http://test"), Timeout = TimeSpan.FromMilliseconds(250) };
        var api = new LeaveApi(http);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SubmitAsync(Payload, stop.Token));
        else await Assert.ThrowsAsync<ApiFailure>(() => api.SubmitAsync(Payload, stop.Token));
        Assert.Equal(1, calls);
    }
}
