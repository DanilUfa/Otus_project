using Xunit;
using VacationPlanner.Application.Services;
using VacationPlanner.UnitTests.Fakes;

namespace VacationPlanner.UnitTests;

/// <summary>
/// Фоновая чистка refresh-токенов: точный оборотный порог (now - grace) передаётся
/// репозиторию; grace из конфигурации, дефолт 1 день, отрицательные значения клампятся.
/// </summary>
public class RefreshTokenCleanupServiceTests
{
    private static void AssertNear(DateTime actual, DateTime expected, double seconds = 5)
        => Assert.InRange(actual, expected.AddSeconds(-seconds), expected.AddSeconds(seconds));

    private static (FakeRefreshTokenRepository Repo, RefreshTokenCleanupService Svc) New(
        params (string Key, string Value)[] config)
    {
        var repo = new FakeRefreshTokenRepository();
        return (repo, new RefreshTokenCleanupService(repo, new FakeConfiguration(config)));
    }

    [Fact]
    public async Task Default_grace_is_one_day()
    {
        var (repo, svc) = New();
        var before = DateTime.UtcNow;
        await svc.CleanupAsync();
        Assert.NotNull(repo.LastRevokedBefore);
        AssertNear(repo.LastRevokedBefore!.Value, before.AddDays(-1));
    }

    [Fact]
    public async Task Grace_days_from_configuration()
    {
        var (repo, svc) = New(("RefreshTokenCleanup:CleanupGraceDays", "3"));
        var before = DateTime.UtcNow;
        await svc.CleanupAsync();
        AssertNear(repo.LastRevokedBefore!.Value, before.AddDays(-3));
    }

    [Fact]
    public async Task Negative_grace_is_clamped_to_zero()
    {
        var (repo, svc) = New(("RefreshTokenCleanup:CleanupGraceDays", "-5"));
        var before = DateTime.UtcNow;
        await svc.CleanupAsync();
        AssertNear(repo.LastRevokedBefore!.Value, before);
    }

    [Fact]
    public async Task Zero_grace_cuts_off_at_now()
    {
        var (repo, svc) = New(("RefreshTokenCleanup:CleanupGraceDays", "0"));
        var before = DateTime.UtcNow;
        await svc.CleanupAsync();
        AssertNear(repo.LastRevokedBefore!.Value, before);
    }

    [Fact]
    public async Task Returns_number_of_deleted_records()
    {
        var (repo, svc) = New();
        repo.DeletedCountToReturn = 11;
        Assert.Equal(11, await svc.CleanupAsync());
    }
}
