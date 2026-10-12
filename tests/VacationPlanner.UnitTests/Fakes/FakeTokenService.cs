using VacationPlanner.Application.Contracts;
using VacationPlanner.Domain.Entities;

namespace VacationPlanner.UnitTests.Fakes;

/// <summary>Детерминированный access-токен: \"at:{userId}\" — подписи/сроки не нужны.</summary>
public sealed class FakeTokenService : ITokenService
{
    public string CreateAccessToken(User user) => "at:" + user.Id;
}
