using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using VacationPlanner.Importer.Csv;

namespace VacationPlanner.Importer.Http;

internal sealed class ApiFailure(string message) : Exception(message);
internal sealed record ApiResult(int? LeaveId, RowIssue? Error);

internal sealed class LeaveApi(HttpClient client)
{
    // This claim is only a local consistency check. The API authenticates the signed JWT.
    public static int ReadOwner(string token)
    {
        var parts = token.Split('.');
        if (parts.Length == 3)
        {
            try
            {
                using var json = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty(ClaimTypes.NameIdentifier, out var id) &&
                    id.ValueKind == JsonValueKind.String && int.TryParse(id.GetString(), out var owner) && owner > 0)
                    return owner;
            }
            catch (Exception ex) when (ex is JsonException or FormatException) { }
        }
        throw new ArgumentException("Expected a Vacation Planner access token with a user identifier.");
    }

    public async Task<ApiResult> SubmitAsync(LeavePayload payload, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("api/leave", payload, ct);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var leave = await response.Content.ReadFromJsonAsync<Receipt>(ct);
                if (leave is { Id: > 0 } && leave.UserId == payload.UserId) return new(leave.Id, null);
                throw new ApiFailure("Invalid receipt from POST /api/leave; verify dispatch.");
            }
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound
                or HttpStatusCode.UnprocessableEntity)
            {
                // Only a recognized API envelope is a definite rejection. Never persist arbitrary upstream text.
                var problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
                if (problem?.Error is { Length: > 0 } error)
                {
                    string message = error switch
                    {
                        _ when error.Contains("пересекается", StringComparison.OrdinalIgnoreCase) => "Leave dates overlap.",
                        _ when error.Contains("лимит", StringComparison.OrdinalIgnoreCase) => "Annual allowance exceeded.",
                        _ when error.Contains("задним числом", StringComparison.OrdinalIgnoreCase) => "Annual leave starts in the past.",
                        _ => "Vacation Planner rejected the record."
                    };
                    return new(null, new($"HTTP{(int)response.StatusCode}", message));
                }
            }
            throw new ApiFailure($"POST /api/leave returned HTTP {(int)response.StatusCode}; verify dispatch.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ApiFailure("POST /api/leave timed out; verify dispatch."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or NotSupportedException)
        { throw new ApiFailure("Could not confirm POST /api/leave; verify dispatch."); }
    }

    private sealed record Receipt(int Id, int UserId);
    private sealed record Problem(string? Error);
}
