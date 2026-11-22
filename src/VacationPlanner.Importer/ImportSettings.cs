namespace VacationPlanner.Importer;

internal sealed class ImportSettings
{
    public string BaseUrl { get; init; } = "http://localhost:5000/";
    public string Token { get; init; } = "";
    public int Workers { get; init; } = 4;
    public int Capacity { get; init; } = 16;
    public int TimeoutSeconds { get; init; } = 30;
    public int HeartbeatSeconds { get; init; } = 2;
    public int StaleSeconds { get; init; } = 60;
    public int MaxRows { get; init; } = 100000;
    public long MaxFileBytes { get; init; } = 16 * 1024 * 1024;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Invalid Importer:BaseUrl.");
        if (Workers is < 1 or > 64 || Capacity is < 1 or > 4096 || TimeoutSeconds is < 1 or > 300 ||
            HeartbeatSeconds is < 1 or > 300 || StaleSeconds <= HeartbeatSeconds || MaxRows < 1 ||
            MaxFileBytes is < 1 or > 268435456)
            throw new ArgumentException("Invalid importer limits, timeout or heartbeat configuration.");
    }
}
